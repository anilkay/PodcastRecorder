# Podcast Recorder

Windows için podcast kayıt uygulaması. Mikrofonu, çalma listesindeki şarkıları ve jingle panelini uygulamanın kendi mikserinde birleştirir ve tek bir MP3 (veya WAV) dosyasına kaydeder. Windows "Stereo Mix" ayarı gerekmez.

## Özellikler

- Mikrofon ve dinleme (kulaklık) cihazı seçimi (WASAPI)
- Kaydet / duraklat / durdur, süre sayacı; MP3 128/192/320 kbps, WMA 128/192 kbps veya WAV
- Mikser: Mikrofon, Müzik, Jingle kanalları; seviye, sessiz, "kulaklıkta duy", VU metre ve clip uyarısı; Master (kayıt) ve Kulaklık seviyesi
- Jingle paneli: 4x4 hücre, sürükle-bırak ya da sağ tık ile dosya ata, tıkla ya da tuşuna basarak çal/durdur
- Çalma listesi: ekle, sırala, çal, sonraki, bitince otomatik sonrakine geç
- Klasör gezgini: dosyaları listeye ya da jingle hücresine sürükle
- Canlı yayın: kayda giden ses (Master) bir Icecast sunucusuna MP3 olarak gönderilir; kayıttan bağımsız açılıp kapanır
- Klavye kısayolları (uygulamada F1 ile listelenir)
- Ayarlar `%APPDATA%\PodcastRecorder\settings.json` dosyasında saklanır; yayın şifresi Windows kullanıcısına özel olarak şifrelenir (DPAPI)

Mikrofon varsayılan olarak kulaklığa verilmez (konuşurken kendi sesinizi gecikmeli duymamanız için) ama her zaman kayda girer.

### Klavye kısayolları

| Tuş | İşlem |
| --- | --- |
| Ctrl+R | Kaydı başlat |
| Ctrl+P | Duraklat / devam |
| Ctrl+Shift+R | Kaydı durdur ve kaydet |
| Ctrl+M | Mikrofonu kapat / aç |
| Ctrl+L / Ctrl+Shift+L | Yayına başla / yayını durdur |
| F5 / F6 / F7 | Çalma listesi: çal / durdur / sonraki |
| 1 2 3 4, Q W E R, A S D F, Z X C V | Jingle hücreleri (paneldeki yerleriyle aynı) |
| Esc | Bütün jingle'ları durdur |
| F1 | Kısayol listesi |

Durdurma tuşları kazara basılmasın diye Shift ister.

### Canlı yayın

"Canlı yayın" panelindeki **Ayarlar…** ile sunucu, port, mount (ör. `/canli`), kullanıcı (Icecast'te genelde `source`), şifre ve kaliteyi girin. Yayın Icecast 2.4+ kaynak protokolüyle (HTTP PUT) gönderilir; çalma listesinde şarkı değişince dinleyicilerin gördüğü başlık güncellenir. Bağlantı yavaşlarsa ses kuyrukta birikmez, bir kısmı atlanır ve panelde uyarı görünür; bağlantı koparsa yayın kapanır (otomatik yeniden bağlanma yok).

### WMA

WMA, Windows Media Foundation kodlayıcısıyla yazılır. Media Foundation dosyayı bütün olarak kodladığından kayıt önce yanına geçici bir WAV (`.wma.kayit.wav`) olarak yazılır ve durdurulunca WMA'ya çevrilir; çevirme başarısız olursa kayıt `.wav` olarak saklanır. Windows Server'da "Media Foundation" özelliği gerekebilir.

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

## Yol haritası

- **M2:** Ducking (mikrofon açılınca müziği kısma), crossfade, jingle sekmeleri
- **M3:** Çoklu mikrofon, FLAC ve multitrack kayıt
