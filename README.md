# devlog

Yapılacak işleri ve günlük çalışma kaydını tutan yerel bir komut satırı aracı. Tek bir SQLite
dosyasında çalışır; sunucu, hesap veya ağ bağlantısı istemez.

İki tür kayıt tutar:

- **todo** — sonra yapılacak işler. Kaydederken repo, branch ve commit bilgisi bulunulan
  dizinden otomatik dolar; iş tamamlanınca kapatılır.
- **worklog** — gün içinde ne yapıldığı ve ne kadar süre harcandığı. Tarihe yazılan bir not
  defteridir, kapatma kavramı yoktur.

Her iki kayıt türüne ekran görüntüsü iliştirilebilir. Kayıtlar tarayıcı arayüzünden de
görüntülenip düzenlenebilir.

Claude Code ile birlikte çalışır ama **ona bağımlı değildir**: kayıt tutan kısım Claude
kurulu olmayan bir makinede de aynen çalışır.

## Kurulum

Gereken tek şey **.NET 9 SDK**'dır (`dotnet tool install` SDK ister, runtime yetmez):

```bash
dotnet --list-sdks
```

Elinizdeki `DevLog.Cli.<sürüm>.nupkg` dosyasını bir klasöre koyup o klasörde:

```bash
dotnet tool install --global --add-source . DevLog.Cli
```

```bash
devlog help
```

`command not found` alırsanız PATH eksiktir. macOS/Linux'ta:

```bash
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zshrc && source ~/.zshrc
```

## Kullanım

Kısa adı `dl`; `devlog` ile aynı işi yapar.

```bash
devlog todo add "Ödeme servisinde retry eklenecek" --desc "Zaman aşımında tek deneme yapılıyor" --priority high
```

```bash
devlog todo list --repo .
```

```bash
devlog todo done T-12 --by "PR 42"
```

```bash
devlog work add "Sipariş iptalinde çift tahsilat giderildi" --minutes 85 --wi "AB#1234"
```

```bash
devlog work week
```

Tarayıcı arayüzü — listeleme, düzenleme, silme, rapor ve ekran görüntüleri:

```bash
devlog ui
```

Komutların tamamı ve bütün parametreler için:

```bash
devlog help
```

## Claude Code entegrasyonu (isteğe bağlı)

```bash
devlog setup
```

Bu komut üç şey yapar:

- `~/.claude/skills/todo` ve `~/.claude/skills/worklog` altına skill dosyalarını yazar —
  böylece `/todo` ve `/worklog` komutları kullanılabilir hale gelir.
- `~/.claude/CLAUDE.md` içine neyin kaydedileceğini anlatan bölümü ekler. Dosyanın geri
  kalanına dokunmaz; yalnızca `<!-- devlog:start -->` ve `<!-- devlog:end -->` işaretleri
  arasını yönetir.
- `~/.claude/settings.json` içine üç hook ekler (oturum açılışı, kapanışı ve duraklama).
  Mevcut ayarlarınız korunur, yalnızca eksik olan hook'lar eklenir; değiştirmeden önce
  `.devlog-yedek` uzantılı bir yedek alınır.

Ayarların geçerli olması için kurulumdan sonra **yeni bir Claude Code oturumu** açmak gerekir.

Neyin kurulu olduğunu görmek için:

```bash
devlog setup status
```

Kayıt kuralları: todo yalnızca kullanıcı onayıyla açılır, çalışma günlüğü **asla onaysız
yazılmaz**. Kurallar `src/DevLog.Cli/Assets/` altındaki üç markdown dosyasında tanımlıdır ve
pakete gömülüdür; `devlog setup` bu dosyaların o anki içeriğini yazar.

## Verileriniz nerede

| Ne | Nerede |
|---|---|
| Veritabanı | `~/.devlog/devlog.db` |
| Ekran görüntüleri | `~/.devlog/attachments/` |

`DEVLOG_DB` ortam değişkeniyle veritabanı yolu değiştirilebilir. Veritabanında tam dosya yolu
tutulmaz, yalnızca dosya adı tutulur; klasörü taşımak kayıtları bozmaz.

Kayıtlar yerel makinede kalır, hiçbir yere gönderilmez.

## Kaldırma

Önce entegrasyon, sonra araç:

```bash
devlog setup remove
```

```bash
dotnet tool uninstall --global DevLog.Cli
```

Kayıtlarınız `~/.devlog/` içinde durmaya devam eder; istemiyorsanız o klasörü elle silin.

## Kaynaktan derleme

```bash
dotnet build src/DevLog.Cli/DevLog.Cli.csproj
```

Windows'ta `update.ps1` derleme, paketleme ve kurulumu tek adımda yapar; sürüm numarasını
otomatik bir artırır ve dosyaları kilitleyen devlog süreçlerini önce kapatır:

```powershell
.\update.ps1 -Open
```

Diğer platformlarda:

```bash
dotnet pack src/DevLog.Cli/DevLog.Cli.csproj -c Release -o nupkg
```

Arayüz tek bir dosyadır (`src/DevLog.Cli/Ui.html`) ve derleme sırasında pakete gömülür;
ayrı bir derleme adımı veya bağımlılığı yoktur.
