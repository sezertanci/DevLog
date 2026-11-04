# devlog — yapılacak iş ve çalışma günlüğü kaydı

Lokal `devlog` (kısası `dl`) komutu, repodan bağımsız iki kayıt tutar: **todo** (sonra yapılacak
işler) ve **worklog** (gün içinde ne yapıldığı + harcanan süre). Detaylı kullanım: `devlog help`.

## Todo — ne kaydedilir
Konuşma sırasında aşağıdakilerden biri geçtiğinde kullanıcıya **kısaca sor ve onay alırsan** kaydet:
- Bilinçli olarak kapsam dışı bırakılan iş ("şimdilik böyle kalsın", "sonra bakarız")
- Kabul edilen teknik borç veya bilinen eksik
- Elle çalıştırılması gereken SQL/script, canlıda yapılacak backfill
- Deploy sonrasında açılacak yorum satırları, iki fazlı işin ikinci fazı
- Migration, cache temizliği gibi kod dışında kalan takip işleri

Kaydederken repo, branch ve commit otomatik dolar; ayrıca varsa iş maddesini (`--wi AB#1234`),
önerilen çözümü (`--suggestion`) ve ilgili dosyaları (`--files`) da geç.

## Todo — ne kaydedilmez
- O anda zaten yapılan iş
- Kullanıcının istemediği spekülatif iyileştirme
- Repo'nun kendisinin zaten kaydettiği şeyler (kod, git geçmişi, AGENTS.md)
- Aynı işin ikinci kez kaydı — önce `devlog todo list --repo .` ile kontrol et

## Todo — kapatma
Bir madde gerçekten yapıldıysa `devlog todo done T-<id> --by "<commit veya PR>"` çalıştır.
Vazgeçilen madde `drop`, gereksiz madde `rm -y` ile silinir. Kullanıcı istemeden silme.

## Worklog — kural
Çalışma günlüğü **asla onaysız yazılmaz**. Kullanıcı `/worklog` dediğinde veya kaydetmek
istediğini söylediğinde: oturumu özetle, süreyi hesapla, öneriyi göster, onay bekle, sonra yaz.
Süre tahmindir; kullanıcının verdiği rakam her zaman üstündür.

## Yazım — kayıtlar tam Türkçe tutulur
Başlık, açıklama, öneri ve worklog metinleri **diyakritikleri dahil doğru Türkçe** yazılır:
"ödeme", "sipariş", "müşteri", "değişiklik". ASCII'ye indirgeme ("odeme", "siparis") yapma.
devlog uçtan uca UTF-8'dir; Türkçe karakter geçmek sorunsuz çalışır, kısıtlayan bir şey yok.

Olduğu gibi bırakılacaklar — bunlar Türkçe kelime değil, tanımlayıcıdır:
- Kod tanımlayıcıları, dosya yolları, endpoint ve tablo/kolon adları
- Branch, ortam ve yapılandırma adları (`mac-uyumlulugu`, `durumle`)
- Dış sistemden alıntılanan literal metinler (entegrasyon yanıtları, panel etiketleri)

## Genel
- Kullanıcıya kaydı hatırlatırken tek ve kısa bir cümle kullan, listeyi uzatma.
- `devlog` komutu çalışmazsa sessizce geç, kullanıcıyı meşgul etme.
