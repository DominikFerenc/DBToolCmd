# Firebird DB Tool (.NET)

Narzędzie konsolowe ułatwiające eksport struktur bazy danych Firebird do plików SQL oraz budowanie i aktualizację baz danych na podstawie katalogu skryptów.

## Wymagania

- .NET 8+
- Firebird 3+ (lub kompatybilny)
- Dostęp do pliku bazy danych lub hosta Firebird

## Instalacja

Sklonuj repozytorium i przejdź do katalogu projektu:

```
git clone https://github.com/twoj-repo/firebird-db-tool.git
cd firebird-db-tool
```

## Budowanie projektu

```
dotnet build
```

## Uruchomienie aplikacji

```
dotnet run -- [polecenie] [argumenty]
```

# Dostępne polecenia

## 1. export-scripts

Eksportuje metadane z istniejącej bazy danych do plików .sql.

### Przykład:

```
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=123;Database=C:\db\test.fdb;DataSource=localhost;" --output-dir "C:\MojeSkrypty"
```

### Działanie:

- Łączy się z podaną bazą danych.
- Tworzy foldery:
  - 1_domains/
  - 2_tables/
  - 3_procedures/
- Każdy obiekt zapisuje w osobnym pliku .sql.
- Procedury w formacie CREATE OR ALTER PROCEDURE.

## 2. build-db

Tworzy nową pustą bazę danych i wykonuje skrypty SQL z podanego katalogu.

### Przykład:

```
dotnet run -- build-db --db-dir "C:\db_temp" --scripts-dir "C:\MojeSkrypty"
```

### Działanie:

- Tworzy nowy plik bazy danych w C:\db_temp.
- Wykonuje skrypty domen, tabel i procedur.

Uwaga: Nie używaj lokalizacji takich jak Pulpit/Dokumenty.

## 3. update-db

Aktualizuje istniejącą bazę danych, używając skryptów z katalogu.

### Przykład:

```
dotnet run -- update-db --connection-string "User=SYSDBA;Password=podajhaslo;Database=C:\db\test.fdb;DataSource=localhost;" --scripts-dir "C:\MojeSkrypty"
```

### Działanie:

- Łączy się z istniejącą bazą danych.
- Wykonuje skrypty SQL.
- Procedury CREATE OR ALTER działają bezpiecznie.

# Ograniczenia i uproszczenia

### Obsługiwane obiekty:

- Domeny
- Tabele
- Procedury

### Pomijane:

- Indeksy
- Klucze obce
- Klucze główne
- Constraints
- Widoki
- Triggery

### Dodatkowe informacje:

- Eksport tabel zawiera tylko kolumny i typy danych.
- Brak porównywania schematów (diff).
- CREATE DOMAIN i CREATE TABLE rzucają błąd, jeśli istnieją.

