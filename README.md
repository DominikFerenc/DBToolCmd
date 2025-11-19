# DbMetaTool

**DbMetaTool** to prosta aplikacja konsolowa dla platformy **.NET 8.0**,
służąca do zarządzania metadanymi baz danych **Firebird 5.0**.\
Umożliwia:

- eksportowanie struktury istniejącej bazy do skryptów SQL,
- budowanie nowej bazy na podstawie tych skryptów,
- aktualizowanie istniejącej bazy ("delta update") poprzez analizę
  różnic między skryptami a rzeczywistą strukturą.

Zakres obsługiwanych obiektów został uproszczony --- narzędzie
obsługuje:

- **Domeny**
- **Tabele**
- **Procedury**

Pozostałe obiekty (triggery, indeksy, więzy integralności, widoki itp.)
są ignorowane.

---

## Wymagania

- **.NET 8.0 SDK**
- Działający serwer **Firebird 5.0** (np. `localhost:3050`)
- Poprawnie ustawione hasło do użytkownika `SYSDBA` w
  `Program.cs → BuildDatabase` (domyślnie używane: `123`)

---

## Budowanie i uruchamianie

1.  Sklonuj repozytorium lub skopiuj pliki narzędzia do jednego
    katalogu.
2.  Otwórz terminal w tym katalogu.

### Przywracanie zależności

```bash
dotnet restore
```

### Budowanie projektu (opcjonalne)

```bash
dotnet build
```

### Uruchamianie aplikacji

```bash
dotnet run -- [polecenie] [argumenty]
```

---

## Dostępne polecenia

---

## 1. `export-scripts`

Eksportuje metadane istniejącej bazy danych do skryptów SQL.

### Przykład użycia

```bash
dotnet run -- export-scripts   --connection-string "User=SYSDBA;Password=123;Database=C:\TEST.FDB;DataSource=localhost;"   --output-dir "C:\MojeSkrypty"
```

### Działanie

- Tworzy katalogi:
  - `1_domains`
  - `2_tables`
  - `3_procedures`
- Generuje pliki `.sql` dla każdego obiektu.
- Procedury są eksportowane jako **CREATE OR ALTER PROCEDURE**.

---

## 2. `build-db`

Tworzy nową, pustą bazę danych i wykonuje na niej skrypty z podanego
katalogu.

### Przykład użycia

```bash
dotnet run -- build-db   --db-dir "C:\db_temp"   --scripts-dir "C:\Users\domin\OneDrive\Pulpit\MojeSkrypty"
```

### Działanie

- Tworzy nowy plik bazy danych (np. `NewDb_yyyyMMddHHmmss.fdb`) w
  podanym katalogu.
- Uruchamia skrypty SQL w ściśle ustalonej kolejności.
- Pierwszy napotkany błąd przerywa proces.

---

## 3. `update-db` --- Inteligentna Aktualizacja („Delta Update")

Aktualizuje istniejącą bazę danych poprzez analizę różnic między
strukturą bazy a skryptami.

### Przykład --- aktualizacja na żywo

```bash
dotnet run -- update-db   --connection-string "User=SYSDBA;Password=123;Database=C:\db\TEST.FDB;DataSource=localhost;"   --scripts-dir "C:\MojeSkrypty"
```

### Przykład --- symulacja (Dry Run)

```bash
dotnet run -- update-db   --connection-string "User=SYSDBA;Password=123;Database=C:\db\TEST.FDB;DataSource=localhost;"   --scripts-dir "C:\MojeSkrypty"   --dry-run
```

---

## Zaawansowane działanie

Narzędzie analizuje różnice między skryptami a bazą danych:

### Domeny

- Dodawanie nowych domen.
- Rozszerzanie istniejących (np. `VARCHAR(50)` → `VARCHAR(100)`).

### Tabele

- Tworzenie nowych tabel.
- Dodawanie nowych kolumn (`ALTER TABLE ADD`).
- Usuwanie kolumn niewystępujących w skryptach (`ALTER TABLE DROP`).

### Procedury

- Aktualizacja kodu przy użyciu `CREATE OR ALTER`.
- Obsługa zależności między procedurami poprzez inteligentny mechanizm
  ponawiania (`Retry Loop`).

---

## Ograniczenia

- Obsługiwane są tylko:
  - Domeny
  - Tabele
  - Procedury
- Ignorowane są:
  - Indeksy
  - Klucze obce
  - Widoki
  - Wyzwalacze
- Parser SQL jest uproszczony i oparty na **regexach** --- działa
  poprawnie dla skryptów generowanych przez narzędzie, lecz może
  wymagać dostosowań dla niestandardowych formatów.

---
