using System.IO;

namespace DiskRadar;

/// <summary>Labels a path as system / program / cleanable / personal. Unmatched children inherit the parent's label.</summary>
public static class SafetyRules
{
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();

    static readonly HashSet<string> CacheNames = new()
    { "cache", "cache2", "code cache", "gpucache", "shadercache", "dawncache", "grshadercache", "dawnwebgpucache", "cachestorage" };

    static readonly HashSet<string> PersonalNames = new()
    { "documents", "desktop", "pictures", "videos", "music", "onedrive", "belgeler", "masaüstü", "resimler" };

    public static void Classify(Node n, Node? parent)
    {
        var p = n.FullPath.TrimEnd('\\').ToLowerInvariant();
        var name = n.Name.ToLowerInvariant();
        var parentPath = Path.GetDirectoryName(p) ?? "";
        bool atRoot = parent != null && parent.Parent == null && parent.FullPath.Length <= 3;
        bool underUsers = p.Contains(@"\users\");

        (Safety s, string note)? r = null;

        if (name is "pagefile.sys" or "swapfile.sys")
            r = (Safety.System, "Sanal bellek dosyası. Windows yönetir; elle silinmez.");
        else if (name == "hiberfil.sys")
            r = (Safety.System, "Hazırda bekletme dosyası. Kapatmak için yönetici komut satırında: powercfg /h off");
        else if (p == Win)
            r = (Safety.System, "Windows sistem klasörü. Dokunmayın.");
        else if (p == Win + @"\temp")
            r = (Safety.Cleanable, "Windows geçici dosyaları. İçeriği güvenle silinebilir.");
        else if (p == Win + @"\softwaredistribution\download")
            r = (Safety.Cleanable, "İndirilmiş Windows Update paketleri. 'Disk Temizleme' ile silinebilir.");
        else if (p == Win + @"\winsxs")
            r = (Safety.System, "Windows bileşen deposu. Elle silmeyin; 'Disk Temizleme > Sistem dosyalarını temizle' ile küçülür.");
        else if (p == Win + @"\installer")
            r = (Safety.System, "Program kaldırma/onarım dosyaları. Silinirse programlar kaldırılamaz.");
        else if (name == "system volume information")
            r = (Safety.System, "Sistem geri yükleme noktaları. Ayarlar > Sistem > Sistem koruması'ndan yönetilir.");
        else if (name == "$recycle.bin")
            r = (Safety.Cleanable, "Geri dönüşüm kutusu. Boşaltarak yer açabilirsiniz.");
        else if (atRoot && name is "windows.old" or "$windows.~bt" or "$windows.~ws")
            r = (Safety.Cleanable, "Eski Windows kurulumu / yükseltme artığı. 'Disk Temizleme' ile silinir.");
        else if (atRoot && name is "recovery" or "boot" or "$winreagent" or "$sysreset" or "bootmgr")
            r = (Safety.System, "Kurtarma / önyükleme dosyaları. Dokunmayın.");
        else if (atRoot && name == "programdata")
            r = (Safety.Program, "Programların ortak verileri. Elle silmeyin.");
        else if (atRoot && name.StartsWith("program files"))
            r = (Safety.Program, "Yüklü programlar. Silmek yerine Ayarlar > Uygulamalar'dan kaldırın.");
        else if (atRoot && name == "users")
            r = (Safety.Personal, "Kullanıcı klasörleri.");
        else if (underUsers && p.EndsWith(@"\appdata\local\temp"))
            r = (Safety.Cleanable, "Geçici dosyalar. Güvenle silinebilir (açık programların kullandıkları atlanır).");
        else if (underUsers && p.EndsWith(@"\appdata\local\crashdumps"))
            r = (Safety.Cleanable, "Program çökme dökümleri. Silinebilir.");
        else if (underUsers && p.EndsWith(@"\microsoft\windows\inetcache"))
            r = (Safety.Cleanable, "İnternet önbelleği. Silinebilir.");
        else if (underUsers && n.IsDir && CacheNames.Contains(name))
            r = (Safety.Cleanable, "Uygulama önbelleği. İlgili program kapalıyken silinebilir; gerekirse yeniden oluşur.");
        else if (n.IsDir && name == "node_modules")
            r = (Safety.Cleanable, "npm paketleri. Proje klasöründe 'npm install' ile yeniden oluşturulur.");
        else if (n.IsDir && name == ".vs")
            r = (Safety.Cleanable, "Visual Studio geçici verileri. Silinebilir.");
        else if (underUsers && (p.EndsWith(@"\.nuget\packages") || p.EndsWith(@"\.gradle\caches") ||
                                p.EndsWith(@"\appdata\local\npm-cache") || p.EndsWith(@"\pip\cache") ||
                                p.EndsWith(@"\yarn\cache") || p.EndsWith(@"\nuget\v3-cache")))
            r = (Safety.Cleanable, "Paket yöneticisi önbelleği. Silinebilir; gerekince yeniden indirilir.");
        else if (underUsers && p.EndsWith(@"\appdata\local\programs"))
            r = (Safety.Program, "Bu kullanıcıya yüklenmiş programlar. Ayarlar > Uygulamalar'dan kaldırın.");
        else if (underUsers && name == "appdata")
            r = (Safety.Program, "Programların ayar ve verileri. Tanımadığınız alt klasörleri silmeyin.");
        else if (underUsers && name == "downloads" && parentPath.Count(c => c == '\\') == 2)
            r = (Safety.Personal, "İndirilenler: genelde eski kurulum dosyaları birikir, gözden geçirin.");
        else if (underUsers && PersonalNames.Contains(name) && parentPath.Count(c => c == '\\') == 2)
            r = (Safety.Personal, "Kişisel dosyalarınız. Karar sizin.");

        if (r != null) { n.Safety = r.Value.s; n.Note = r.Value.note; }
        else if (parent != null) { n.Safety = parent.Safety; n.Note = parent.Note; }
    }
}
