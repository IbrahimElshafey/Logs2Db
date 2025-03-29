# Logs2Db

Logs2Db is a cross-platform tool for parsing unstructured or semi-structured logs, extracting fields via regex patterns, and writing them to a SQLite database (or other databases) using Dapper. The tool comes with an Avalonia-based UI where you can define your regex patterns, database tables, and post-processing scripts — all saved in a project file for easy reuse.

---

## Key Features

- **Cross-Platform UI (Avalonia)**  
  Runs on Windows, macOS, and Linux.

- **Dynamic Regex Patterns**  
  Define regexes with named groups; the tool compiles them for fast matching.

- **Flexible Table Definitions**  
  Decide on columns, map named groups to them, and specify ID handling (auto-generate or from logs).

- **Custom C# Hooks (Optional)**  
  If needed, embed advanced logic when mapping fields to columns.

- **Post-Processing SQL**  
  Run scripts on your generated database (e.g., for bulk updates, conditional transformations, or final aggregations).

- **Lightweight & Performant**  
  Powered by Dapper for minimal overhead and high performance, with parallel parsing support for large log files.

---

## How the Project File Works

Logs2Db uses a **Project File (JSON)** to store your entire parsing configuration. You can create or modify this file within the application itself:

1. **Regex Patterns Window**  
   - Add or edit regex patterns with named groups.
   - Define how each group maps to table columns (e.g., `TxId -> TransactionUpsertRecord:TxId`).
   - Configure optional vs. required groups, specify ID auto-generation, etc.

2. **Tables Window**  
   - Set up table definitions, their columns, and ID handling rules.
   - Decide if the ID comes from a regex group or is auto-generated (e.g., GUID, integer).

3. **Post Processing Scripts Window**  
   - Add SQL scripts that will run after all insert/upsert operations finish.
   - Perfect for conditional updates, setting columns only if they’re default, computing aggregates, etc.

All these settings are saved into the **Project File**. When you reopen Logs2Db with the same file, your previous patterns, tables, and scripts are instantly available for parsing new logs.

---

## Quick Start

1. **Clone or Download**  
   ```bash
   git clone https://github.com/IbrahimElshafey/Logs2Db.git
