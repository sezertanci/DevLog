---
name: worklog
description: Bu oturumda yapılan işleri özetleyip harcanan süreyle birlikte çalışma günlüğüne kaydeder (devlog work). Kullanıcı "/worklog", "bugün ne yaptım", "bunu günlüğe kaydet", "süreyi kaydet", "haftalık rapor" dediğinde kullan.
---

# devlog worklog

Gün içinde ne yapıldığını ve ne kadar süre harcandığını kaydeder. Todo değildir — burada
"kapatma" yoktur, tarihe yazılan bir not defteridir.

## Kaydetme akışı (varsayılan iş)

**Kayıt asla onaysız yazılmaz.** Sıra şudur:

1. **Süreyi hesapla.** Oturumun transcript'inden aktif süreyi çıkar:
   ```
   devlog session time --project <proje klasörü>
   ```
   `<proje klasörü>`, `~/.claude/projects` altındaki bulunulan repoya ait klasör adıdır
   (örn. `C--projeler-ornek-api`). Parametre vermezsen en son yazılan oturumu alır.
   Çıktıdaki `activeMinutes` 15 dakikadan uzun boşluklar düşülmüş süredir; `blocks` ise
   molalarla ayrılmış çalışma aralıklarını verir. Eşiği `--gap 20` ile değiştirebilirsin.

2. **Oturumu konu bloklarına böl.** Konuşmada kaç ayrı iş yapıldıysa onları ayır. Aktif süreyi
   bu işlere, konuşmadaki ağırlıklarına göre dağıt. Tek iş varsa bölme.

3. **Öneriyi göster ve onay al.** Şu formatta, kısa:
   ```
   1) Sipariş iptalinde çift tahsilat giderildi   ~85 dk   ornek-api/master          AB#1234
   2) Rapor ekranına tarih filtresi eklendi       ~40 dk   ornek-api/feature/1235    AB#1235
   ```
   Kullanıcı düzeltirse (süre değiştirme, birleştirme, satır çıkarma) düzelt ve tekrar göster.
   **Kullanıcının verdiği süre her zaman hesaplanan süreden üstündür.**

4. **Onaydan sonra her satır için:**
   ```
   devlog work add "<özet>" --minutes <dk> --detail "<detay>" --wi "AB#1234" --todos "T-12" --session "<oturum id>"
   ```
   - `--session` değerini transcript dosya adından al (uzantısız hali oturum kimliğidir);
     bu, aynı oturumun ikinci kez kaydedilmesini önler.
   - O oturumda bir todo açıldıysa veya kapandıysa `--todos T-12,T-13` ile bağla.
   - Geçmiş bir gün için: `--date 2026-09-09` veya `--date yesterday`.
   - Saat aralığı biliniyorsa `--minutes` yerine `--start 09:30 --end 11:05` de kullanılabilir.

5. Kaydedilen `W-<id>` numaralarını tek satırda bildir.

## Görüntüleme

- Bugün: `devlog work today`
- Bu hafta: `devlog work week`
- Aralık: `devlog work list --from 2026-09-01 --to 2026-09-10`
- Rapor: `devlog work report --from ... --to ... --group-by day|repo|workitem`
- Ekran (tarayıcı arayüzü): `devlog ui`

Çıktıyı olduğu gibi göster, yeniden biçimlendirme.

## Düzeltme ve silme

- Düzeltme: `devlog work edit W-<id> --minutes 45 --summary "..." --branch feature/20059`
  Sadece verdiğin alanlar değişir. `--start`/`--end` birlikte verilirse ve `--minutes`
  verilmezse süre yeniden hesaplanır. Değiştirilebilen alanlar: `--summary --detail --date
  --minutes --start --end --repo --branch --wi --todos`
- Kalıcı silme: `devlog work rm W-<id> -y` — önce `devlog work show W-<id>` ile göster ve onay al.

## Kurallar

- Kullanıcı istemeden günlüğe yazma. Stop hook hatırlatma yaptığında bile **sormak** yeterlidir.
- Özet tek cümle olsun, teknik ve somut: "Sipariş iptalinde çift tahsilat giderildi" gibi.
  "Çeşitli düzeltmeler yapıldı" gibi boş özet yazma.
- Özet ve detayı **diyakritikleri dahil doğru Türkçe** yaz ("ödeme", "sipariş"); ASCII'ye
  indirgeme. devlog uçtan uca UTF-8, Türkçe karakter sorun çıkarmaz. Kod tanımlayıcıları,
  dosya yolları, branch/ortam adları ve dış sistem literalleri olduğu gibi kalır.
- Süre bir tahmindir; öneri olarak sun, kesinmiş gibi sunma.
