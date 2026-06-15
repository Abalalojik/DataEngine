# DataEngine

The data layer for [InkSwap](https://github.com/Abalalojik/InkSwap) and the wider Gravity Project.

DataEngine bundles two pieces:

- **DBEngine** — a multipurpose embedded local database supporting both SQL-style
  **Tables** and NoSQL-style **Collections** (documents), with indexed fields,
  cross-references, runtime schema evolution (add/drop/rename fields), and record history.
- **dbeg-mcp** — an MCP (Model Context Protocol) server that exposes DBEngine to AI agents:
  open/close, scan/find/get, insert/update/delete, and schema operations including
  `db_create_container`.

## Layout

| Path | What |
|------|------|
| `DBEngine/` | the engine (class library) + `DBEngine.Tests` |
| `dbeg-mcp/` | the MCP server (references DBEngine) |
| `DataEngine.slnx` | the solution |

## Build

```sh
dotnet build DataEngine.slnx
```

---

*Part of the Gravity Project. Single source for the engine + its MCP.*
