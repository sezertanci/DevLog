namespace DevLog;

public static class Help
{
    public static int Print()
    {
        Console.WriteLine($@"devlog - lokal yapilacak is (todo) ve calisma gunlugu (worklog) kaydi
Veritabani: {Db.DbPath}

TODO
  devlog todo add ""baslik"" [--desc ...] [--suggestion ...] [--wi AB#1234]
                            [--files ...] [--priority high|normal|low]
                            [--repo ...] [--branch ...] [--no-git]
  devlog todo list [--all] [--status open|done|dropped] [--repo .] [--branch .]
                   [--wi ...] [--limit 50] [--offset 0] [--json]
  devlog todo show T-12 [--json]
  devlog todo edit T-12 [--title ...] [--desc ...] [--suggestion ...] [--branch ...]
                        [--repo ...] [--wi ...] [--files ...]
                        [--priority ...] [--status ...]
        # sadece verilen alanlar degisir. --branch . bulundugun branch'i yazar.
        # bir alani temizlemek icin bos deger ver:  --wi ""
  devlog todo done T-12 [--by ""PR 42""]
  devlog todo drop T-12
  devlog todo rm   T-12 -y          # kalici silme, -y olmadan silmez

WORK (calisma gunlugu)
  devlog work add ""ne yapildi"" --minutes 45 [--date today] [--start 09:30 --end 11:00]
                                [--detail ...] [--wi AB#1234] [--todos T-12,T-13]
                                [--session <id>] [--repo .] [--no-git]
  devlog work today [--json]
  devlog work week  [--json]
  devlog work list  [--from 2026-09-01] [--to 2026-09-10] [--repo .] [--limit 100] [--offset 0] [--json]
  devlog work show W-7 [--json]
  devlog work edit W-7 [--minutes 45] [--summary ...] [--detail ...] [--date ...]
                       [--start 09:30 --end 11:00] [--repo ...] [--branch ...]
                       [--wi ...] [--todos ...]
  devlog work rm   W-7 -y           # kalici silme, -y olmadan silmez
        # tarih bicimleri: yyyy-MM-dd, dd.MM.yyyy, today/bugun, yesterday/dun
  devlog work report [--from ...] [--to ...] [--group-by day|repo|workitem] [--json]

EKRAN GORUNTULERI (todo ve worklog kayitlarina eklenir)
  devlog attach add  T-18 ekran.png [--caption ""..""]
  devlog attach list T-18 [--json]
  devlog attach rm   A-5 -y
        # dosyalar <DB klasoru>/attachments altinda tutulur, kayit silinince onlar da silinir

EKRAN
  devlog ui [--port 5177] [--no-browser]   # tarayici arayuzu: liste, kapatma, silme, rapor

CLAUDE CODE ENTEGRASYONU
  devlog setup            # hook'lari, skill'leri ve kural dosyasini kurar (mevcut ayarlari ezmez)
  devlog setup status     # neyin kurulu oldugunu gosterir
  devlog setup remove     # temiz sekilde kaldirir (kayitlariniz silinmez)

OTURUM
  devlog session start --session <id> [--transcript <path>] [--quiet]
  devlog session end   --session <id> [--quiet]
  devlog session show  --session <id> [--json]
  devlog session time  [--project <klasor>] [--transcript <yol>] [--gap 15]
        # transcript'ten aktif calisma suresini hesaplar (molalar dusulur)

HOOK (Claude Code cagirir, girdi stdin'den JSON)
  devlog hook session-start | session-end | stop

Repo ve branch bilgisi bulunulan dizinden git ile otomatik doldurulur.
'.' degeri her yerde 'bulundugum repo/branch' anlamina gelir.
DEVLOG_DB ortam degiskeni ile veritabani yolu degistirilebilir.");
        return 0;
    }
}
