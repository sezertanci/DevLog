---
name: todo
description: Sonra yapılacak işleri kaydeder, listeler ve kapatır (devlog todo). Kullanıcı "/todo", "bunu kaydet", "not düş", "açık işlerim neler", "şunu tamamlandı yap" veya "bu maddeyi sil" dediğinde kullan.
---

# devlog todo

Lokal `devlog` CLI üzerinden yapılacak iş kaydı. Repo, branch ve commit bilgisi bulunulan
dizinden otomatik dolar — `--repo` / `--branch` vermene gerek yok.

## Ne istendiğini ayırt et

**Kaydetme** ("bunu kaydet", "sonra yaparız", "/todo <açıklama>")
1. Önce mükerrer kontrolü: `devlog todo list --repo . --json`
2. Konuşmadan şu alanları çıkar: başlık (kısa ve eylem cümlesi), açıklama (neden ertelendi),
   öneri (nasıl çözülür, hangi dosya), iş maddesi, ilgili dosyalar, öncelik.
3. Kaydedeceğin maddeyi kullanıcıya **tek blok halinde göster ve onay al.**
4. Onaydan sonra:
   ```
   devlog todo add "<başlık>" --desc "<açıklama>" --suggestion "<öneri>" --wi "AB#1234" --files "<yollar>" --priority high|normal|low
   ```
5. Dönen `T-<id>` numarasını kullanıcıya söyle.

**Listeleme** ("açık işlerim", "bu repoda ne var")
- Bulunulan repo: `devlog todo list --repo .`
- Tümü: `devlog todo list --all`
- Filtreler: `--branch .`, `--wi AB#1234`, `--status done|dropped`
- Çıktıyı olduğu gibi göster; tabloyu yeniden biçimlendirme.

**Detay**: `devlog todo show T-<id>`

**Düzenleme** ("branch yanlış", "bunu yüksek önceliğe al", "açıklamayı düzelt")
- `devlog todo edit T-<id> --branch feature/20059 --priority high`
- Sadece verdiğin alanlar değişir, diğerleri korunur.
- `--branch .` bulunduğun branch'i yazar; `--repo .` aynı şekilde.
- Bir alanı temizlemek için boş değer ver: `--wi ""`
- Değiştirilebilen alanlar: `--title --desc --suggestion --repo --branch --commit
  --wi --files --priority --status`

**Ekran görüntüsü ekleme** ("şu ekranı da ekle", "görüntüyü iliştir")
- `devlog attach add T-<id> <dosya yolu> --caption "<kısa açıklama>"`
- Listeleme: `devlog attach list T-<id>` · Silme: `devlog attach rm A-<id> -y` (önce onay al)
- Worklog kayıtları için de aynı komut: `devlog attach add W-<id> ...`
- Kullanıcı arayüzden de yapıştırabilir; elinde bir dosya yolu varsa sen ekleyebilirsin.

**Kapatma** ("bunu yaptık", "tamamlandı")
- `devlog todo done T-<id> --by "<commit sha veya PR numarası>"`
- Commit/PR referansını biliyorsan mutlaka `--by` ile geç.

**Vazgeçme**: `devlog todo drop T-<id>` (kayıt durur, durumu `dropped` olur)

**Kalıcı silme**: `devlog todo rm T-<id> -y`
- Önce `show` ile ne silineceğini kullanıcıya göster ve **açık onay al.**
- `-y` olmadan komut silmez, çıkış kodu 2 döner.

## Kurallar
- Kullanıcı açıkça istemedikçe kayıt oluşturma; kendiliğinden en fazla **öneri** yap.
- Bir oturumda aynı işi ikinci kez kaydetme.
- Silme ve kapatma her zaman kullanıcı onayıyla yapılır.
- Başlık, açıklama ve öneriyi **diyakritikleri dahil doğru Türkçe** yaz ("ödeme", "sipariş");
  ASCII'ye indirgeme. devlog uçtan uca UTF-8, Türkçe karakter sorun çıkarmaz. Kod tanımlayıcıları,
  dosya yolları, branch/ortam adları ve dış sistem literalleri olduğu gibi kalır.
- Ayrıntılı kullanım için: `devlog help`
