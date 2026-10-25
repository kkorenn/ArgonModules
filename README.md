# ArgonModule

Argon modules for A Dance of Fire and Ice.

| Module | Description |
|--------|-------------|
| [Tuf](Tuf) | TUF (tuforums.com) level browser |
| [ExampleModule](ExampleModule) | Template for new modules |

## Build

Set `ADOFAI_PATH` to your game folder (defaults to the author's path), then:

```bash
dotnet build -c Release Tuf/Tuf.csproj
```

The `.armodule` is copied to `$ADOFAI_PATH/UserData/Argon/Modules`. Pass `-p:NoDeploy=true` to skip the copy.

## Releases

Tag per module, e.g. `tuf-v1.2`.
