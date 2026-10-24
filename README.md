# Podcast Recorder

Windows için podcast kayıt uygulaması. Mikrofonu, çalma listesindeki şarkıları ve jingle panelini uygulamanın kendi mikserinde birleştirir ve tek bir MP3 (veya WAV) dosyasına kaydeder. Windows "Stereo Mix" ayarı gerekmez.

## Özellikler

- Birden fazla mikrofon: "Mikrofon ekle" ile her konuk için ayrı kanal, ✕ ile kaldırma; dinleme (kulaklık) cihazı seçimi (WASAPI)
- Kaydet / duraklat / durdur, süre sayacı; MP3 128/192/320 kbps, WAV veya FLAC (kayıpsız)
- **Ayrı izler (multitrack):** Karışıma ek olarak her kanal (her mikrofon, müzik, jingle) `podcast-TARİH izler` klasörüne ayrı dosya olarak kaydedilir; kurguda konuşmacıları ayrı ayrı düzeltmek için
- Mikser: Mikrofon, Müzik, Jingle kanalları; seviye, sessiz, "kulaklıkta duy", VU metre ve clip uyarısı; Master (kayıt) ve Kulaklık seviyesi
- Jingle paneli: 4x4 hücre, sürükle-bırak ya da sağ tık ile dosya ata, tıkla çal/durdur
- Çalma listesi: ekle, sırala, çal, sonraki, bitince otomatik sonrakine geç
- Klasör gezgini: dosyaları listeye ya da jingle hücresine sürükle
- Ayarlar `%APPDATA%\PodcastRecorder\settings.json` dosyasında saklanır
- **Mikrofon AÇIK/KAPALI düğmesi:** Kapalıyken mikrofon kayda girmez; açıkken ducking etkinse müzik ayarlanan seviyeye (-3 ile -30 dB arası) yumuşakça kısılır. Jingle'lar kısılmaz.
- **Ducking modu:** "Mikrofon açıkken" (düğme açık olduğu sürece) ya da "Konuşunca (otomatik)": müzik yalnızca mikrofona konuşulurken kısılır, susunca geri gelir. Konuşma eşiği ayarlanabilir.
- **Limiter:** Kayıt -1 dBFS'yi geçmesin diye tepe seviyeleri anlık kısar; kırpılma (clip) olmaz.
- **LUFS ölçer:** Kayıt çubuğunda anlık ve kaydın tamamı için ses yüksekliği (EBU R128 / ITU-R BS.1770-4). Podcast'ler için yaygın hedef -16 LUFS.
- **Normalizasyon:** İşaretlenirse kayıt bitince ses yüksekliği ölçülür ve dosya -16 LUFS'e getirilir (kayıt sırasında geçici bir kayıpsız WAV tutulur, sonra seçilen formatta yazılır).
- **Crossfade:** Çalma listesinde şarkılar 0-10 saniye bindirilerek geçer; otomatik sonrakine geçişte de, elle "Sonraki" ya da başka şarkı çalındığında da uygulanır.
- **Jingle sekmeleri:** Her program için ayrı sekme; sekme adı ve ızgara boyutu (2x2 ile 8x8 arası) değiştirilebilir.

Mikrofon varsayılan olarak kulaklığa verilmez (konuşurken kendi sesinizi gecikmeli duymamanız için) ama her zaman kayda girer.

## Gereksinimler

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Çalıştırma

```powershell
dotnet run --project src/PodcastRecorder.App
```

Testler:

```powershell
dotnet test
```

## Sürüm çıkarmak

Sürüm numarası `Directory.Build.props` içindeki `<Version>` alanındadır. `v` ile başlayan bir tag push edildiğinde GitHub Actions testleri çalıştırır, uygulamayı .NET kurulumu gerektirmeyen (self-contained) win-x64 olarak yayınlar ve zip'i GitHub Releases'a ekler:

```powershell
git tag v0.0.1
git push origin v0.0.1
```

## Proje yapısı

| Proje | İçerik |
| --- | --- |
| `src/PodcastRecorder.Audio` | Ses motoru: mikser, kaynaklar (mikrofon, çalma listesi, jingle), kayıt ve encoder'lar. NAudio üzerine kurulu. |
| `src/PodcastRecorder.App` | WPF arayüzü (MVVM, CommunityToolkit.Mvvm) |
| `tests/PodcastRecorder.Audio.Tests` | Ses motoru birim testleri (xUnit) |

### Ses akışı

```
Mikrofon ─┐                    ┌─> Program bus ─> Recorder ─> IAudioEncoder (MP3/WAV) ─> dosya
Müzik ────┼─> AudioMixer ──────┤
Jingle ───┘                    └─> Monitor bus ─> WasapiOut (kulaklık)
```

Tüm kaynaklar 48 kHz stereo float formatına çevrilir. Çıkış cihazının saati akışı sürer; kodlama ayrı bir arka plan görevinde yapılır, böylece disk yazımı sesi takıltmaz.

### Yeni çıktı formatı eklemek

`IAudioEncoder` arayüzünü uygulayın ve `EncoderCatalog.All` listesine ekleyin; arayüzdeki format listesi otomatik güncellenir.
