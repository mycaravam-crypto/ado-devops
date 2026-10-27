# Installation

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Running needs `git` on your `PATH`.

## Install as a global .NET tool (recommended)

From the cloned repository root, with .NET 10 SDK installed:

```bash
dotnet pack src/AdoCli -c Release -o artifacts
dotnet tool install --global AdoCli --add-source ./artifacts
ado --version
```

For an existing installation, use `dotnet tool update --global AdoCli --add-source ./artifacts`.
If an installed version is newer than the locally built version, uninstall it first or pack a higher version.
Make sure `$HOME/.dotnet/tools` is on your `PATH` (restart your shell if necessary).

The existing publish instructions below remain available for standalone binaries.

## 1. Compile

`dotnet publish` creates a single `ado` executable (`ado.exe` on Windows). Choose the runtime identifier for your platform:

| Platform | `-r` |
|---|---|
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Windows x64 | `win-x64` |

```bash
# Needs the .NET 10 runtime on the machine (small binary)
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o out

# Runs without .NET installed (larger binary, can be copied to other machines with the same platform)
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o out
```

The executable ends up in `out/`. To update later, pull and run the same command again.

## 2. Use it from bash (Linux, macOS, WSL, Git Bash)

Publish straight into a directory on your `PATH`, for example `~/.local/bin`:

```bash
dotnet publish src/AdoCli -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ~/.local/bin
```

If `~/.local/bin` is not on your `PATH` yet, add it to `~/.bashrc` (on macOS with zsh: `~/.zshrc`) and reload:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
ado --version
```


## 3. Use it from PowerShell (Windows)

Publish into a folder of your own and add that folder to your user `PATH` once:

```powershell
$bin = "$env:LOCALAPPDATA\Programs\ado"
dotnet publish src/AdoCli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $bin

# Add the folder to the user PATH permanently (only needed once)
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $bin) {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$bin", 'User')
}
$env:Path += ";$bin"   # current session; new terminals pick it up automatically

ado --version
```


**PowerShell 7 on Linux or macOS:** install the binary as in the bash section above. Then add the folder to `$env:PATH`
in `$PROFILE`: `$env:PATH = "$HOME/.local/bin:$env:PATH"`.

Shell defaults, scripting and the token: [configuration.md](configuration.md#shell-defaults-and-scripts).

## Getting help

```bash
ado --help                    # overview: commands, global flags, environment variables, exit codes
ado workitem --help           # one command's subcommands, flags and examples (also: ado workitem, ado help workitem)
ado pr merge --help           # same as ado pr --help
```
