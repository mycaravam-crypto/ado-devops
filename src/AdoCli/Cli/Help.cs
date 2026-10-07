namespace AdoCli.Cli;

/// <summary>Console help: an overview for 'ado --help', and one page per command group for 'ado &lt;command&gt; --help'.</summary>
public static class Help
{
    public const string Overview = """
        ado - a small CLI for Azure DevOps Server

        Usage:
          ado <command> <subcommand> [arguments] [flags]
          ado <command> --help      subcommands, flags and examples of one command

        Commands:
          auth      login, status, logout
          config    list, get, set, unset
          repo      list, show, clone, status
          pr        list, show, context, diff, checkout, create, approve, merge
          workitem  list, show, create, edit
          build     list, show, run

        Inside a cloned Azure DevOps repository, project and repository are detected
        from the origin remote. Otherwise pass --project <name> and --repo <name>,
        or set a default project with ADO_PROJECT.

        Settings are taken from flags, then environment variables (ADO_SERVER, ADO_PAT,
        ADO_PROJECT, ADO_API_VERSION, ADO_INSECURE, ADO_CA_CERT, ADO_PROXY), then
        ~/.ado/config.json. 'ado config list' shows which one applies.

        Global flags:
          --project <name>  Azure DevOps project to use
          --repo <name>     repository to use
          --json            machine-readable output on stdout; messages go to stderr
          --debug           log HTTP requests and show stack traces
          --insecure        skip TLS certificate checks (internal servers only);
                            --insecure=false overrides a saved setting
          --ca-cert <file>  also trust the CA certificate(s) in this PEM file;
                            --ca-cert none overrides a saved one
          --limit <n>       list commands fetch at most n items (default: all)
          --proxy <url>     use this HTTP(S) or SOCKS proxy; --proxy none connects
                            directly (default: HTTPS_PROXY or system settings)
          --help, -h        show help
          --version         show version

        Exit codes:
          0 success   1 general failure   2 invalid usage   3 not logged in
          4 permission denied   5 not found   6 conflict
        """;

    const string Auth = """
        ado auth - log in to Azure DevOps Server

        Usage:
          ado auth login <server-url>   log in with a personal access token (prompts for it,
                                        or reads it from stdin); checks it before saving
          ado auth status               show server, user, TLS mode and proxy in effect
          ado auth logout               remove stored credentials (~/.ado/config.json)

        The token needs the scopes Code read & write, Work items read & write and
        Build read & execute. Instead of logging in, CI can set ADO_SERVER and ADO_PAT.

        Examples:
          ado auth login https://tfs.company.local/tfs/DefaultCollection
          echo "$PAT" | ado auth login https://tfs.company.local/tfs/DefaultCollection
        """;

    const string Config = """
        ado config - view and change settings

        Usage:
          ado config list               show every setting, its value and where it comes from
          ado config get <key>          print one setting (exit 1 if not set)
          ado config set <key> <value>  save a setting in ~/.ado/config.json
          ado config unset <key>        remove a setting from ~/.ado/config.json

        Keys (environment variable in brackets):
          server      server URL, e.g. https://tfs.company.local/tfs/DefaultCollection [ADO_SERVER]
          project     default project [ADO_PROJECT]
          apiVersion  REST API version, default 5.0 [ADO_API_VERSION]
          insecure    true to skip TLS certificate checks [ADO_INSECURE]
          caCert      PEM file with extra CA certificates to trust [ADO_CA_CERT]
          proxy       proxy URL, or none for a direct connection [ADO_PROXY]
        The token is set only by 'ado auth login' [ADO_PAT].

        Examples:
          ado config set caCert ~/company-ca.pem
          ado config set proxy http://proxy.company.local:8080
          ado config unset insecure
        """;

    const string Repo = """
        ado repo - repositories

        Usage:
          ado repo list                 list repositories (of the current project, if known)
          ado repo show [<repo>]        show repository details
          ado repo clone <repo> [dir]   clone a repository with git (uses the TLS and proxy settings)
          ado repo status               show the server, project and repository of the current clone

        Examples:
          ado repo list --project Platform
          ado repo clone backend
          ado repo status --json
        """;

    const string Pr = """
        ado pr - pull requests

        Usage:
          ado pr list                   list pull requests of the repository (or of the project
                                        outside a clone) with their target branch
              --status <s>              active (default), completed, abandoned or all
              --mine                    only pull requests you created
          ado pr show <id>              show a pull request with its reviewers' votes
          ado pr context <id>           pull request with commits, changed files and work items,
                                        as JSON
          ado pr diff <id>              show the changes with git; --json lists changed files
          ado pr checkout <id>          check out as local branch pr/<id> (fast-forwards if it exists)
          ado pr create                 create a pull request; prompts unless --title is given
              --title <t> --description <d>
              --source <branch>         default: the current branch
              --target <branch>         default: the repository's default branch
          ado pr approve <id>           approve a pull request
          ado pr merge <id>             complete a pull request after confirming
              --squash                  squash the commits
              --yes, -y                 do not ask

        diff and checkout run git and must run inside a clone of the PR's repository.

        Examples:
          ado pr list --mine --status all
          ado pr create --title "Fix login" --target develop
          ado pr merge 142 --squash --yes
        """;

    const string WorkItem = """
        ado workitem - work items

        Usage:
          ado workitem list             list work items assigned to you, open ones by default
          ado workitem show <id>        show a work item with every field
          ado workitem create           create a work item; prompts unless --type and --title are given
          ado workitem edit <id>...     change fields of one or more work items; - reads ids from stdin

        list filters (all must match):
          --all                         everyone's items, not just yours
          --assigned-to <who>           assigned to who (@me for you)
          --type <type>                 e.g. Bug; repeat or comma-separate for several
          --state <state>               e.g. Active,Resolved; 'any' includes closed items
          --area <path>                 under this area path
          --iteration <path>            under this iteration path
          --tag <tag>                   with this tag; repeat for several (all must match)
          --title-contains <text>       text in the title (ignoring case)
          --contains <text>             text in the title or the description
          --wiql <condition>            your own WIQL condition
          --ids                         print only ids, one per line, for 'workitem edit -'

        Fields, for create and edit:
          --type <type>                 (create only) e.g. Bug, Task, "User Story"
          --title <t>  --description <d>  --state <s>  --assigned-to <who> ("" unassigns)
          --area <path>  --iteration <path>  --tags "a; b" (replaces existing tags)
          --comment <text>              add a comment to the discussion
          --field Name=value            any field by reference name; can be repeated

        edit only:
          --replace-title <old> --with <new>   replace text in each title; titles without it are skipped
          --yes, -y                     do not ask before changing several items
                                        (required when stdin is not a terminal)
          --dry-run                     print what would change, change nothing

        Examples:
          ado workitem list --type Bug --state Active,Resolved
          ado workitem list --all --state any --contains timeout
          ado workitem create --type Task --title "Update docs" --field Microsoft.VSTS.Common.Priority=2
          ado workitem edit 4711 --state Active --assigned-to jane@company.local --comment "Picked up"
          ado workitem list --all --type Bug --tag xyz --ids | ado workitem edit - --state Closed --yes
          ado workitem list --all --state any --title-contains abc --ids |
            ado workitem edit - --replace-title abc --with xyz --dry-run
        """;

    const string Build = """
        ado build - builds

        Usage:
          ado build list                list builds of the project, newest first
          ado build show <id>           show a build
          ado build run <definition-id> queue a build
              --branch <branch>         default: the definition's default branch

        Examples:
          ado build list --limit 10
          ado build run 12 --branch feature/login
        """;

    /// <summary>The help page of a command group ("pr", "workitem", ...), or null for an unknown one.</summary>
    public static string? For(string? command) => command switch
    {
        "auth" => Auth,
        "config" => Config,
        "repo" => Repo,
        "pr" => Pr,
        "workitem" => WorkItem,
        "build" => Build,
        _ => null,
    };
}
