using System.Buffers.Binary;
using System.Text;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// Chapters in an MP4/M4A file, written the way ffmpeg writes them: a disabled QuickTime text track
/// the audio track points to with a "chap" reference (Apple Podcasts, iTunes, QuickTime), and a Nero
/// "chpl" list in moov/udta (VLC, mpv, foobar2000, many Android players). The chapter names go
/// into a new mdat at the end of the file; the rest of the audio is copied unchanged, only its
/// chunk offsets move if moov sits in front of it.
/// </summary>
internal static class Mp4Chapters
{
    private const uint Timescale = 1000;

    /// <summary>The boxes this code looks inside; everything else is kept as opaque bytes.</summary>
    private static readonly HashSet<string> Containers = ["moov", "trak", "mdia", "minf", "stbl", "udta", "tref", "edts", "dinf"];

    /// <summary>Marks a text sample as UTF-8 (QuickTime "encd" atom, as ffmpeg adds it).</summary>
    private static readonly byte[] Encd = [0, 0, 0, 12, (byte)'e', (byte)'n', (byte)'c', (byte)'d', 0, 0, 1, 0];

    public static void Write(string path, IReadOnlyList<Chapter> chapters)
    {
        List<TopBox> top;
        Box moov;
        long length;
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            length = input.Length;
            top = ScanTopLevel(input);
            var moovEntry = top.SingleOrDefault(b => b.Type == "moov") ?? throw new InvalidDataException("No moov box.");
            var bytes = new byte[moovEntry.Size - moovEntry.HeaderSize];
            input.Position = moovEntry.Offset + moovEntry.HeaderSize;
            input.ReadExactly(bytes);
            moov = new Box("moov", bytes, Containers);
        }

        var oldMoov = top.Single(b => b.Type == "moov");
        RemoveChapters(moov);

        var mvhd = moov.Child("mvhd") ?? throw new InvalidDataException("No mvhd box.");
        var (movieScale, movieDuration) = ReadScaleAndDuration(mvhd.Data!);
        var audio = moov.Children("trak").FirstOrDefault(t => Handler(t) == "soun")
                    ?? throw new InvalidDataException("No audio track.");

        byte[] samples = [];
        List<int> sizes = [];
        Box? chapterTrak = null;
        if (chapters.Count > 0)
        {
            uint trackId = NextTrackId(mvhd.Data!);
            SetNextTrackId(mvhd.Data!, trackId + 1);
            (samples, sizes) = Samples(chapters);
            long totalMs = movieScale == 0 ? 0 : (long)(movieDuration * 1000.0 / movieScale);
            var durations = Durations(chapters, totalMs);
            bool wide = length + 1_000_000 + samples.Length > uint.MaxValue;
            chapterTrak = ChapterTrak(trackId, movieDuration, totalMs, durations, sizes, wide);

            var tref = audio.Child("tref");
            if (tref is null)
            {
                tref = Box.Container("tref");
                audio.Insert(audio.IndexOf("tkhd") + 1, tref);
            }

            tref.Add(Box.Leaf("chap", U32(trackId)));
            moov.Add(chapterTrak);

            var udta = moov.Child("udta");
            if (udta is null)
            {
                udta = Box.Container("udta");
                moov.Add(udta);
            }

            udta.Add(Box.Leaf("chpl", Chpl(chapters)));
        }

        // Laying out: the new moov replaces the old one; anything after it moves by the difference.
        long newMoovSize = moov.Size;
        long delta = newMoovSize - oldMoov.Size;
        long oldMoovEnd = oldMoov.Offset + oldMoov.Size;
        foreach (var trak in moov.Children("trak"))
        {
            if (ReferenceEquals(trak, chapterTrak)) continue;
            ShiftChunkOffsets(trak, oldMoovEnd, delta);
        }

        long chapterData = length + delta + 8;
        if (chapterTrak is not null)
        {
            var offsets = new List<long>();
            long offset = chapterData;
            foreach (var size in sizes)
            {
                offsets.Add(offset);
                offset += size;
            }

            SetChunkOffsets(chapterTrak, offsets);
        }

        var temp = path + ".bolumler.tmp";
        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var box in top)
                {
                    if (ReferenceEquals(box, oldMoov))
                    {
                        moov.WriteTo(output);
                        continue;
                    }

                    input.Position = box.Offset;
                    if (box.SizeToEnd)
                    {
                        // "Runs to the end of the file" no longer holds once chapters follow it.
                        if (box.Size > uint.MaxValue) throw new NotSupportedException("Box too large.");
                        output.Write(U32((uint)box.Size));
                        output.Write(Encoding.ASCII.GetBytes(box.Type));
                        input.Position += 8;
                        CopyBytes(input, output, box.Size - 8);
                    }
                    else
                    {
                        CopyBytes(input, output, box.Size);
                    }
                }

                if (samples.Length > 0)
                {
                    output.Write(U32((uint)(samples.Length + 8)));
                    output.Write("mdat"u8);
                    output.Write(samples);
                }

                if (output.Length != length + delta + (samples.Length > 0 ? samples.Length + 8 : 0))
                {
                    throw new InvalidDataException("Unexpected layout.");
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Reads the Nero chapter list, which every file written by <see cref="Write"/> has.</summary>
    public static IReadOnlyList<Chapter> Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entry = ScanTopLevel(input).SingleOrDefault(b => b.Type == "moov");
        if (entry is null) return [];
        var bytes = new byte[entry.Size - entry.HeaderSize];
        input.Position = entry.Offset + entry.HeaderSize;
        input.ReadExactly(bytes);
        var chpl = new Box("moov", bytes, Containers).Child("udta")?.Child("chpl")?.Data;
        if (chpl is null || chpl.Length < 9) return [];

        int pos = chpl[0] == 1 ? 8 : 4;
        int count = chpl[pos++];
        var chapters = new List<Chapter>();
        for (int i = 0; i < count && pos + 9 <= chpl.Length; i++)
        {
            long ticks = BinaryPrimitives.ReadInt64BigEndian(chpl.AsSpan(pos));
            int len = chpl[pos + 8];
            pos += 9;
            if (pos + len > chpl.Length) break;
            chapters.Add(new Chapter(TimeSpan.FromTicks(ticks), Encoding.UTF8.GetString(chpl, pos, len)));
            pos += len;
        }

        return chapters;
    }

    /// <summary>Takes out chapter tracks, the references to them and the Nero list.</summary>
    private static void RemoveChapters(Box moov)
    {
        var chapterIds = new HashSet<uint>();
        foreach (var trak in moov.Children("trak"))
        {
            if (trak.Child("tref") is not { } tref) continue;
            foreach (var chap in tref.Children("chap").ToList())
            {
                for (int i = 0; i + 4 <= chap.Data!.Length; i += 4) chapterIds.Add(BinaryPrimitives.ReadUInt32BigEndian(chap.Data.AsSpan(i)));
                tref.Remove(chap);
            }

            if (tref.ChildCount == 0) trak.Remove(tref);
        }

        foreach (var trak in moov.Children("trak").ToList())
        {
            if (trak.Child("tkhd")?.Data is { } tkhd && chapterIds.Contains(TrackId(tkhd))) moov.Remove(trak);
        }

        if (moov.Child("udta") is { } udta)
        {
            foreach (var chpl in udta.Children("chpl").ToList()) udta.Remove(chpl);
        }
    }

    private static Box ChapterTrak(uint trackId, ulong movieDuration, long totalMs, IReadOnlyList<uint> durations, IReadOnlyList<int> sizes, bool wide)
    {
        int n = durations.Count;

        // Version 1 when the duration does not fit in 32 bits.
        bool v1 = movieDuration > uint.MaxValue;
        var tkhd = new List<byte>();
        tkhd.AddRange(v1 ? [1, 0, 0, 2] : [0, 0, 0, 2]); // flags: in movie, but not enabled (never played)
        tkhd.AddRange(new byte[v1 ? 16 : 8]); // creation, modification
        tkhd.AddRange(U32(trackId));
        tkhd.AddRange(new byte[4]);
        tkhd.AddRange(v1 ? U64(movieDuration) : U32((uint)movieDuration));
        tkhd.AddRange(new byte[8 + 2 + 2 + 2 + 2]); // reserved, layer, alternate group, volume 0, reserved
        tkhd.AddRange(IdentityMatrix());
        tkhd.AddRange(new byte[8]); // width, height

        var mdhd = new List<byte> { 0, 0, 0, 0 };
        mdhd.AddRange(new byte[8]);
        mdhd.AddRange(U32(Timescale));
        mdhd.AddRange(U32((uint)Math.Clamp(totalMs, 0, uint.MaxValue)));
        mdhd.AddRange([0x55, 0xC4, 0, 0]); // language "und"

        var hdlr = new List<byte>(new byte[8]);
        hdlr.AddRange("text"u8.ToArray());
        hdlr.AddRange(new byte[12]);
        hdlr.AddRange("SubtitleHandler\0"u8.ToArray());

        var gmin = new byte[] { 0, 0, 0, 0, 0, 0x40, 0x80, 0, 0x80, 0, 0x80, 0, 0, 0, 0, 0 };
        var text = new byte[] { 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x40, 0, 0, 0 };

        var dref = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 12, (byte)'u', (byte)'r', (byte)'l', (byte)' ', 0, 0, 0, 1 };

        // The stub text sample description ffmpeg writes for chapter tracks.
        var entry = new List<byte>();
        entry.AddRange(new byte[6]);
        entry.AddRange([0, 1]); // data reference index
        entry.AddRange([0, 0, 0, 1, 0, 0, 0, 0, 0, 0]); // display flags, justification, background colour
        entry.AddRange(new byte[8]); // default text box
        entry.AddRange([0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0]); // style record: font 1
        entry.AddRange([0, 0, 0, 13, (byte)'f', (byte)'t', (byte)'a', (byte)'b', 0, 1, 0, 1, 0]); // font table
        var stsd = new List<byte> { 0, 0, 0, 0 };
        stsd.AddRange(U32(1));
        stsd.AddRange(Box.Leaf("text", [.. entry]).ToBytes());

        var stts = new List<byte> { 0, 0, 0, 0 };
        stts.AddRange(U32((uint)n));
        foreach (var d in durations)
        {
            stts.AddRange(U32(1));
            stts.AddRange(U32(d));
        }

        var stsc = new List<byte> { 0, 0, 0, 0 };
        stsc.AddRange(U32(1));
        stsc.AddRange(U32(1)); // from chunk 1 on,
        stsc.AddRange(U32(1)); // one sample per chunk,
        stsc.AddRange(U32(1)); // with description 1

        var stsz = new List<byte> { 0, 0, 0, 0, 0, 0, 0, 0 };
        stsz.AddRange(U32((uint)n));
        foreach (var size in sizes) stsz.AddRange(U32((uint)size));

        var offsets = new List<byte> { 0, 0, 0, 0 };
        offsets.AddRange(U32((uint)n));
        offsets.AddRange(new byte[n * (wide ? 8 : 4)]); // filled in by SetChunkOffsets

        var stbl = Box.Container("stbl");
        stbl.Add(Box.Leaf("stsd", [.. stsd]));
        stbl.Add(Box.Leaf("stts", [.. stts]));
        stbl.Add(Box.Leaf("stsc", [.. stsc]));
        stbl.Add(Box.Leaf("stsz", [.. stsz]));
        stbl.Add(Box.Leaf(wide ? "co64" : "stco", [.. offsets]));

        var gmhd = Box.Container("gmhd");
        gmhd.Add(Box.Leaf("gmin", gmin));
        gmhd.Add(Box.Leaf("text", text));
        var dinf = Box.Container("dinf");
        dinf.Add(Box.Leaf("dref", dref));
        var minf = Box.Container("minf");
        minf.Add(gmhd);
        minf.Add(dinf);
        minf.Add(stbl);

        var mdia = Box.Container("mdia");
        mdia.Add(Box.Leaf("mdhd", [.. mdhd]));
        mdia.Add(Box.Leaf("hdlr", [.. hdlr]));
        mdia.Add(minf);

        var trak = Box.Container("trak");
        trak.Add(Box.Leaf("tkhd", [.. tkhd]));
        trak.Add(mdia);
        return trak;
    }

    /// <summary>Each chapter's name as a text sample: 16-bit length, UTF-8 text, "encd".</summary>
    private static (byte[] Data, List<int> Sizes) Samples(IReadOnlyList<Chapter> chapters)
    {
        var data = new List<byte>();
        var sizes = new List<int>();
        foreach (var chapter in chapters)
        {
            var title = Encoding.UTF8.GetBytes(chapter.Title);
            if (title.Length > ushort.MaxValue) title = title[..ushort.MaxValue];
            data.Add((byte)(title.Length >> 8));
            data.Add((byte)title.Length);
            data.AddRange(title);
            data.AddRange(Encd);
            sizes.Add(2 + title.Length + Encd.Length);
        }

        return ([.. data], sizes);
    }

    /// <summary>How long each chapter lasts, in milliseconds; the last runs to the end of the audio.</summary>
    private static List<uint> Durations(IReadOnlyList<Chapter> chapters, long totalMs)
    {
        var durations = new List<uint>();
        for (int i = 0; i < chapters.Count; i++)
        {
            long start = (long)chapters[i].Start.TotalMilliseconds;
            long end = i + 1 < chapters.Count ? (long)chapters[i + 1].Start.TotalMilliseconds : Math.Max(totalMs, start + 1);
            durations.Add((uint)Math.Clamp(end - start, 1, uint.MaxValue));
        }

        // The track starts at zero, so a first chapter starting later stretches back to it.
        if (chapters.Count > 0 && chapters[0].Start > TimeSpan.Zero)
        {
            durations[0] += (uint)chapters[0].Start.TotalMilliseconds;
        }

        return durations;
    }

    /// <summary>The Nero chapter list: version 1, start times in 100 ns, names up to 255 bytes.</summary>
    private static byte[] Chpl(IReadOnlyList<Chapter> chapters)
    {
        var data = new List<byte> { 1, 0, 0, 0, 0, 0, 0, 0, (byte)Math.Min(chapters.Count, 255) };
        foreach (var chapter in chapters.Take(255))
        {
            data.AddRange(U64((ulong)Math.Max(0, chapter.Start.Ticks)));
            var title = Encoding.UTF8.GetBytes(chapter.Title);
            int len = Math.Min(title.Length, 255);
            // Do not cut a UTF-8 character in half.
            while (len < title.Length && len > 0 && (title[len] & 0xC0) == 0x80) len--;
            data.Add((byte)len);
            data.AddRange(title.AsSpan(0, len).ToArray());
        }

        return [.. data];
    }

    private static void ShiftChunkOffsets(Box trak, long after, long delta)
    {
        if (delta == 0) return;
        var stbl = trak.Child("mdia")?.Child("minf")?.Child("stbl");
        if (stbl is null) return;
        if (stbl.Child("stco")?.Data is { } stco)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(stco.AsSpan(4));
            for (int i = 0; i < count; i++)
            {
                var span = stco.AsSpan(8 + i * 4, 4);
                long value = BinaryPrimitives.ReadUInt32BigEndian(span);
                if (value < after) continue;
                value += delta;
                if (value is < 0 or > uint.MaxValue) throw new NotSupportedException("Chunk offset out of range.");
                BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
            }
        }

        if (stbl.Child("co64")?.Data is { } co64)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(co64.AsSpan(4));
            for (int i = 0; i < count; i++)
            {
                var span = co64.AsSpan(8 + i * 8, 8);
                long value = (long)BinaryPrimitives.ReadUInt64BigEndian(span);
                if (value >= after) BinaryPrimitives.WriteUInt64BigEndian(span, (ulong)(value + delta));
            }
        }
    }

    private static void SetChunkOffsets(Box trak, IReadOnlyList<long> offsets)
    {
        var stbl = trak.Child("mdia")!.Child("minf")!.Child("stbl")!;
        if (stbl.Child("stco")?.Data is { } stco)
        {
            for (int i = 0; i < offsets.Count; i++) BinaryPrimitives.WriteUInt32BigEndian(stco.AsSpan(8 + i * 4), checked((uint)offsets[i]));
        }
        else
        {
            var co64 = stbl.Child("co64")!.Data!;
            for (int i = 0; i < offsets.Count; i++) BinaryPrimitives.WriteUInt64BigEndian(co64.AsSpan(8 + i * 8), (ulong)offsets[i]);
        }
    }

    private static string? Handler(Box trak)
    {
        var hdlr = trak.Child("mdia")?.Child("hdlr")?.Data;
        return hdlr is { Length: >= 12 } ? Encoding.ASCII.GetString(hdlr, 8, 4) : null;
    }

    private static (uint Scale, ulong Duration) ReadScaleAndDuration(byte[] mvhd) => mvhd[0] == 1
        ? (BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(20)), BinaryPrimitives.ReadUInt64BigEndian(mvhd.AsSpan(24)))
        : (BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(12)), BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(16)));

    private static uint NextTrackId(byte[] mvhd) => BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(mvhd.Length - 4));

    private static void SetNextTrackId(byte[] mvhd, uint id) => BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(mvhd.Length - 4), id);

    private static uint TrackId(byte[] tkhd) => BinaryPrimitives.ReadUInt32BigEndian(tkhd.AsSpan(tkhd[0] == 1 ? 20 : 12));

    private static byte[] IdentityMatrix()
    {
        var m = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(16), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(32), 0x40000000);
        return m;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static void CopyBytes(Stream input, Stream output, long count)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0) throw new EndOfStreamException();
            output.Write(buffer, 0, read);
            count -= read;
        }
    }

    private static List<TopBox> ScanTopLevel(Stream input)
    {
        var boxes = new List<TopBox>();
        var header = new byte[16];
        long offset = 0;
        while (offset + 8 <= input.Length)
        {
            input.Position = offset;
            input.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            int headerSize = 8;
            bool toEnd = false;
            if (size == 1)
            {
                input.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = input.Length - offset;
                toEnd = true;
            }

            if (size < headerSize || offset + size > input.Length) throw new InvalidDataException($"Bad box size: {type}.");
            boxes.Add(new TopBox(type, offset, size, headerSize, toEnd));
            offset += size;
        }

        return boxes;
    }

    private sealed record TopBox(string Type, long Offset, long Size, int HeaderSize, bool SizeToEnd);

    /// <summary>A box inside moov: either raw bytes, or (for the types this code walks) child boxes.</summary>
    private sealed class Box
    {
        private readonly List<Box>? _children;

        private Box(string type, byte[]? data, List<Box>? children)
        {
            Type = type;
            Data = data;
            _children = children;
        }

        public Box(string type, byte[] content, HashSet<string> containers)
            : this(type, null, Parse(content, containers))
        {
        }

        public string Type { get; }

        public byte[]? Data { get; }

        public int ChildCount => _children?.Count ?? 0;

        public long Size => 8 + (Data?.Length ?? _children!.Sum(c => c.Size));

        public static Box Leaf(string type, byte[] data) => new(type, data, (List<Box>?)null);

        public static Box Container(string type) => new(type, null, new List<Box>());

        public Box? Child(string type) => _children?.FirstOrDefault(c => c.Type == type);

        public IEnumerable<Box> Children(string type) => _children?.Where(c => c.Type == type) ?? [];

        public int IndexOf(string type) => _children!.FindIndex(c => c.Type == type);

        public void Add(Box box) => _children!.Add(box);

        public void Insert(int index, Box box) => _children!.Insert(index, box);

        public void Remove(Box box) => _children!.Remove(box);

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            WriteTo(stream);
            return stream.ToArray();
        }

        public void WriteTo(Stream output)
        {
            if (Size > uint.MaxValue) throw new NotSupportedException("Box too large.");
            output.Write(U32((uint)Size));
            output.Write(Encoding.ASCII.GetBytes(Type));
            if (Data is not null)
            {
                output.Write(Data);
            }
            else
            {
                foreach (var child in _children!) child.WriteTo(output);
            }
        }

        private static List<Box> Parse(byte[] content, HashSet<string> containers)
        {
            var boxes = new List<Box>();
            int pos = 0;
            while (pos + 8 <= content.Length)
            {
                long size = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(pos));
                var type = Encoding.ASCII.GetString(content, pos + 4, 4);
                int header = 8;
                if (size == 1)
                {
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(content.AsSpan(pos + 8));
                    header = 16;
                }
                else if (size == 0)
                {
                    size = content.Length - pos;
                }

                if (size < header || pos + size > content.Length) throw new InvalidDataException($"Bad box size: {type}.");
                var body = content.AsSpan(pos + header, (int)size - header).ToArray();
                boxes.Add(containers.Contains(type) ? new Box(type, body, containers) : Leaf(type, body));
                pos += (int)size;
            }

            // Trailing padding shorter than a box header (some writers add four zero bytes) is dropped.
            return boxes;
        }
    }
}
