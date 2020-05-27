Troubleshooting
===============

Deleting a VFS for Git repo
---------------------------

Since a VFS for Git clone has a running `GVFS.Mount` process to track the
Git index and watch updates from the ProjFS filesystem driver, you must
first run `gvfs unmount` before deleting your repository. This will also
remove the repository from the auto-mount feature of `GVFS.Service`.

If you have deleted the enlistment or its `.gvfs` folder, then you will
likely see alerts saying "Failed to auto-mount at path `X`". To remove
this enlistment from the auto-mount feature, remove the appropriate line
from the `C:\ProgramData\GVFS\GVFS.Service\repo-registry` file.

Upgrade
-------

The `GVFS.Service` process checks for new versions of VFS for Git daily and
will prompt you for upgrade using a notification. To check manually, run
`gvfs upgrade` to see if an upgrade is available. Run `gvfs upgrade --confirm`
to actually perform the upgrade, if you wish.

Diagnosing Issues
-----------------

The `gvfs diagnose` command collects logs and config details for the current
repository. The resulting zip file helps root-cause issues.

When run inside your repository, creates a zip file containing several important
files for that repository. This includes:

* All log files from `gvfs` commands run in the enlistment, including
  maintenance steps.

* Log files from the `GVFS.Service`.

* Configuration files from your `.git` folder, such as the `config` file,
  `index`, `hooks`, and `refs`.

* A summary of your Git object database, including the number of loose objects
  and the names and sizes of pack-files.

As the `diagnose` command completes, it provides the path of the resulting
zip file. This zip can be sent to the support team for investigation.

Modifying Configuration Values
------------------------------

### Cache Server URL

Cache servers are a feature of the GVFS protocol to provide low-latency
access to the on-demand object requests. This modifies the `gvfs.cache-server`
setting in your local Git config file.

Run `gvfs cache-server --get` to see the current cache server.

Run `gvfs cache-server --list` to see the available cache server URLs.

Run `gvfs cache-server --set=<url>` to set your cache server to `<url>`.

### System-wide Config

The `gvfs config` command allows customizing some behavior.

1. Set system-wide config settings using `gvfs config <key> <value>`.
2. View existing settings with `gvfs config --list`.
3. Remove an existing setting with `gvfs config --delete <key>`.

The `usn.updateDirectories` config option, when `true`, will update the
[USN journal entries](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-usn)
of directories when the names of subdirectories or files are modified,
even if the directory is still only in a "projected" state. This can be
particularly important when using incremental build systems such as
microsoft/BuildXL. However, there is a 10-15% performance penalty on some
Git commands when this option is enabled.

The `gvfs config` command is also used for customizing the feed used for
VFS for Git upgrades. This is so large teams can bundle a custom installer
or other tools along with VFS for Git upgrades.
