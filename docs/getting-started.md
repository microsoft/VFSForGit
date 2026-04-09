Getting Started
===============

Repository Requirements
-----------------------

VFS for Git will work with any Git service that supports the
[GVFS protocol](/Protocol.md). For example, you can create a repo in
[Azure DevOps](https://azure.microsoft.com/services/devops/), and push
some contents to it. There are two constraints:

  * Your repo must not enable any clean/smudge filters
  * Your repo must have a `.gitattributes` file in the root that includes
    the line `* -text`


Cloning 
-------

The `clone` verb creates a local enlistment of a remote repository using the
[GVFS protocol](https://github.com/microsoft/VFSForGit/blob/master/Protocol.md).

```
gvfs clone [options] <url> [<dir>]
```

Create a local copy of the repository at `<url>`. If specified, create the `<dir>`
directory and place the repository there. Otherwise, the last section of the `<url>`
will be used for `<dir>`. At the end, the repo is located at `<dir>/src`.

### Options

These options allow a user to customize their initial enlistment.

* `--cache-server-url=<url>`: If specified, set the intended cache server to
  the specified `<url>`. All object queries will use the GVFS protocol to this
  `<url>` instead of the origin remote. If the remote supplies a list of
  cache servers via the `<url>/gvfs/config` endpoint, then the `clone` command
  will select a nearby cache server from that list.

* `--branch=<ref>`: Specify the branch to checkout after clone.

* `--local-cache-path=<path>`: Use this option to override the path for the
  local VFS for Git cache. If not specified, then a default path inside
  `<Volume>:\.gvfsCache\` is used. The default cache path is recommended so
  multiple clones of the same remote repository share objects on the
  same device.

### Advanced Options

The options below are not intended for use by a typical user. These are
usually used by build machines to create a temporary enlistment that
operates on a single commit.

* `--single-branch`: Use this option to only download metadata for the branch
  that will be checked out. This is helpful for build machines that target
  a remote with many branches. Any `git fetch` commands after the clone will
  still ask for all branches.

* `--no-prefetch`: Use this option to not prefetch commits after clone. This
  is not recommended for anyone planning to use their clone for history
  traversal. Use of this option will make commands like `git log` or
  `git pull` extremely slow and is therefore not recommended.

Mounting and Unmounting
-----------------------

Before running Git commands in your VFS for Git enlistment or reading
files and folders inside the enlistment, a `GVFS.Mount` process must be
running to manage the virtual file system projection.

A mount process is started by a successful `gvfs clone`, and the
enlistment is registered with `GVFS.Service` to auto-mount in the future.

The `gvfs status` command checks to see if a mount process is currently
running for the current enlistment.

The `gvfs mount` command will start a new mount process and register the
enlistment for auto-mount in the future.

The `gvfs unmount` command will safely shut down the mount process and
unregister the enlistment for auto-mount.


Removing a VFS for Git Clone
----------------------------

Since a VFS for Git clone has a running `GVFS.Mount` process to track the
Git index and watch updates from the ProjFS filesystem driver, you must
first run `gvfs unmount` before deleting your repository. This will also
remove the repository from the auto-mount feature of `GVFS.Service`.

If you have deleted the enlistment or its `.gvfs` folder, then you will
likely see alerts saying "Failed to auto-mount at path `X`". To remove
this enlistment from the auto-mount feature, remove the appropriate line
from the `C:\ProgramData\GVFS\GVFS.Service\repo-registry` file.
