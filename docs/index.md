VFS for Git: Virtualized File System for Git
============================================

VFS stands for Virtual File System. VFS for Git virtualizes the file system
beneath your Git repository so that Git and all tools see what appears to be a
regular working directory, but VFS for Git only downloads objects as they
are needed. VFS for Git also manages the files that Git will consider, to
ensure that Git operations such as `status`, `checkout`, etc., can be as quick
as possible because they will only consider the files that the user has
accessed, not all files in the repository.

Installing
----------

* VFS for Git requires Windows 10 Anniversary Update (Windows 10 version 1607) or later
* Run the latest VFS for Git and Git for Windows installers from https://github.com/Microsoft/VFSForGit/releases

Documentation
-------------

* [Getting Started](getting-started.md): Get started with VFS for Git.
  Includes `gvfs clone`.

* [Troubleshooting](troubleshooting.md):
  Collect diagnostic information or update custom settings. Includes
  `gvfs diagnose`, `gvfs config`, `gvfs upgrade`, and `gvfs cache-server`.

* [Frequently Asked Questions](faq.md)
