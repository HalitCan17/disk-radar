<img src="DiskRadar/Assets/app-icon.png" width="96" alt="Disk Radar simgesi">

# Disk Radar

[English](README.md) · **Türkçe**

Üç soruya cevap veren bir Windows disk alanı analiz aracı: **Ne yer kaplıyor, ne zamandır kullanılmıyor ve silmek güvenli mi?**

Disk Radar hiçbir dosyayı silmez veya taşımaz. Bilgiyi gösterir ve klasörü Explorer'da açar; kararı sen verir, silme işlemini kendin yaparsın.

![Gezgin ve kutu haritası](docs/screenshots/01-explorer.png)

## Özellikler

- **Hızlı tarama:** Herhangi bir diski veya klasörü tarar; ilerleme çubuğu, geçen süre ve tahmini kalan süre gösterilir.
- **Güvenlik etiketi:** Her dosya ve klasörde, ne olduğunu ve nasıl temizleneceğini anlatan kısa bir açıklamayla birlikte:
  - 🔴 **Sistem**: dokunma (Windows, sayfa/hazırda bekletme dosyaları, WinSxS…)
  - 🟡 **Program**: silmek yerine *Ayarlar › Uygulamalar*'dan kaldır
  - 🟢 **Silinebilir**: geçici dosyalar, önbellekler, Geri Dönüşüm Kutusu, Windows Update artıkları, derleme çıktıları
  - ⚪ **Kişisel**: senin dosyaların, karar senin
- **Son kullanım** tarihi ve "ne zamandır kullanılmıyor" süresi.
- **Gezgin ve kutu haritası:** Klasörleri tabloda gezersin, yanında boyuta göre kutu haritası; silinebilirler, 1 yıldır kullanılmayanlar, sadece dosyalar gibi filtreler.
- **Temizlenebilir alanlar:** Güvenle temizlenebilecek tüm yerler tek listede, toplamıyla.
- **Dosya türleri:** Video, fotoğraf, arşiv, ISO, sanal disk, kod… hangisi ne kadar yer kaplıyor.
- **Projeler:** .NET, Node.js, Java, Python, Rust, Go… projelerini ve yeniden oluşturulabilir klasörlerini (`bin`, `obj`, `node_modules`, `.venv`, `target`…) bulur.
- **En büyük dosyalar** ve **uzun süredir kullanılmayanlar** (3 ay – 5 yıl).
- **Kopya dosya bulucu:** İçeriği birebir aynı dosyalar (boyut → ilk 64 KB → tam SHA-256).
- **Yüklü programlar:** Boyut, kurulum tarihi ve tahmini son kullanım; *Ayarlar › Uygulamalar* kısayolu.
- **Zaman içindeki değişim:** Her tarama kaydedilir; grafik ve liste hangi klasörün büyüdüğünü ya da küçüldüğünü gösterir.
- **Ada göre arama**, her listeyi **CSV'ye aktarma**, **açık/koyu tema** ve açılışta son taramanın **anında** gösterilmesi.

## Ekran görüntüleri

| Dosya türleri | Uzun süredir kullanılmayanlar |
|---|---|
| ![Dosya türleri](docs/screenshots/02-file-types.png) | ![Kullanılmayanlar](docs/screenshots/04-unused.png) |

| Yüklü programlar | Tarama sürerken |
|---|---|
| ![Yüklü programlar](docs/screenshots/05-installed-apps.png) | ![Tarama](docs/screenshots/06-scan-progress.png) |

![Koyu tema](docs/screenshots/07-dark-theme.png)

## İndir ve çalıştır

[Releases](../../releases) sayfasından `DiskRadar.exe` dosyasını indir. Tek dosyadır; kurulum ve .NET gerektirmez.

Tam sonuç için dosyaya sağ tıklayıp **Yönetici olarak çalıştır**; aksi halde bazı sistem klasörleri okunamaz.

## Kaynaktan derleme

Gereksinimler: Windows 10/11 ve [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
cd DiskRadar
dotnet run -c Release
```

Tek dosyalık .exe'yi `dist/` klasörüne oluşturmak için:

```bash
DiskRadar\publish.cmd
```

## Notlar

- Windows'ta "son erişim" kaydı kapalıysa *son kullanım* için son **değiştirilme** tarihi kullanılır. Uygulamadaki bant hangisinin geçerli olduğunu söyler.
- Yüklü programların *son kullanım* tarihi tahminidir (program klasöründeki en yeni dosya).
- Tarama geçmişi ve son tarama önbelleği `%LOCALAPPDATA%\DiskRadar` içinde tutulur.

## Teknoloji

C# · WPF · .NET 9 (Fluent tema) · harici paket yok.

## Lisans

[LICENSE](LICENSE) dosyasına bakın.
