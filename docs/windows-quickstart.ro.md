# Prima verificare pe Windows 11

Buildul Windows de test este disponibil în
[GitHub Actions](https://github.com/pantyyy17/CARPATIA-ULTIMATE-/actions/runs/42367836867).
După autentificarea în GitHub, descarcă artifactul
`carpatia-ultimate-unsigned-win-x64`, dezarhivează tot și păstrează folderul
`Helper` lângă executabil. Buildul și observarea pornirii au trecut în CI;
verificarea vizuală și Apply/Restore pe PC rămân separate. Este un build de
dezvoltare nesemnat, nu un installer final.

Pentru compilarea surselor și raportul de pe PC-ul tău:

Dezarhivează proiectul într-un folder al tău. Deschide PowerShell normal, fără
„Run as administrator”, în folderul care conține `Carpatia.Ultimate.slnx`.

Rulează:

```powershell
.\scripts\Validate-Windows.ps1
```

Pentru un PC cu procesor ARM, folosește `-Architecture ARM64`. Pentru Intel/AMD,
comanda simplă folosește x64. Scriptul cere SDK-ul din `global.json`: .NET SDK
10.0.403 sau o versiune patch mai nouă din seria 10.0.4xx. Dacă lipsește, raportul
arată acest lucru; scriptul nu instalează programe și nu schimbă execution policy.

Scriptul rulează cele cinci suite de teste, compilează helperul și aplicația și
verifică fișierele necesare. La final afișează o arhivă de forma:

```text
artifacts\Windows-results-<data>-<id>.zip
```

Arhiva se creează și dacă o verificare eșuează. Trimite această arhivă în
conversație pentru diagnosticarea rezultatului Windows. Conține loguri, rapoarte
TRX și `summary.json`, nu întregul executabil sau configurația sistemului.

Scriptul nu lansează aplicația/helperul, nu cere elevare și nu aplică optimizări.
Un build reușit nu validează încă interfața sau restaurarea reală.

Dacă toate verificările trec, folderul aplicației este afișat în consolă.
Păstrează întregul folder, inclusiv `Helper`, când lansezi ulterior aplicația.
Verificările reale Apply/Restore se fac separat într-un mediu de test, conform
`windows-validation.md`. Nu schimba DNS/MTU pe conexiunea care îți asigură
accesul la distanță.

Dacă politica PowerShell blochează scriptul, nu trebuie ocolită pentru a construi:
comenzile individuale sunt în `windows-validation.md` și se pot rula direct în
consolă. Un Visual Studio actual cu instrumentele Windows poate ajuta dacă
buildul raportează o problemă de toolchain; scriptul nu îl impune fără dovezi.
