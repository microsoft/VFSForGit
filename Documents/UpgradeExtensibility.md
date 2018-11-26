VFSForGit Upgrade Extensibility Design Document
===============================

Scope
-----

Enable groups to extend the Upgrade verb to install / update extra
components besides just VFSForGit and Git.

Motivation
----------

VFSForGit can exist as a component of a larger engineering
system. Some environments could include components that are coupled
with specific versions of VFSForGit and should be updated with with
VFSForGit. There could be other interactions specific to the
environment that need to be accounted for when updating VFSForGit. As
these requirements are specific to the individual environments
VFSForGit is being deployed into, we need a way for groups to include
extra components and logic in the VFSForGit upgrade.

Requirements
------------

* Groups can include extra components / logic in VFSForGit Upgrade

* Feed needs to support advertising current available version

Non-Goals
---------

* Initial configuration of the alternate feed URL

* Controlling the entire package of components

Design
------

VFSForGit will support an alternative "Feed" from which updates can be
retrieved. When an alternative feed is configured, VFSForGit will
query this feed for updates instead of the normal feed. This
alternative feed can contain customized components in addition to the
VFSForGit and Git installers.

### Feed

The alternative feed will be a NuGet feed. The feed URL is specified
in the gvfs configuration file (gvfs.config) with the following entry:

    upgrade.feedurl

This is similar to how upgrade rings are specified. The upgrade.feed
will take precedence over the upgrade ring configuration. When this
value is configured, VFSForGit will query and download updates from
this feed. It must be a NuGet feed.

### Upgrade Package Contents

If using the alternative feed, the upgrade package must include
installers for Git and VFSForGit. It can optionally include installers
to run before / after installing Git and GVFS. The upgrade logic will
look in the following locations for the pre / post setup installers:

    PreSetup\install.exe :: Run before the Git and VFSForGit
    			    installers are run.

    PostSetup\install.exe :: Run after the Git and VFSForGit
    			     installers are run.

This means the top level structure of the NuGet package will contain
the following directories:

  G4W :: Contains Git for windows installer
  
  GVFS :: Contains the VFSForGitInstaller
  
  PreSetup :: (Optional) Contains the installer to run before setup
  
  PostSetup :: (Optional) Contains the installer to run after setup

#### Alternative 1

Instead of the package containing 3 different installers, it could
contain a single installer that would be responsible for upgrading
Git, GVFS, and any other components. This would be simpler from the
VFSForGit Upgrade logic, as it would only need to run a single
installer, but the installer generation would be more complex, as it
would need to include logic for all updated packaged - including the
git and VFSForGit installers. As we already have the logic to run the
git and VFSForGit installers, we decided against this option because
it adds more complexity to the installer generation.

#### Alternative 2

Git and GVFS would be upgraded from the public feed as normal, and
only the extra components would be installed from the specified
feed. We decided against this because the components to be distributed
can be coupled, and having two independent feeds could make it
difficult to keep these in sync.

### Upgrade Logic

There are currently two high level components in VFSForGit:

UpgradeOrchestrator :: Responsible for orchestrating the upgrade steps

ProductUpgrader :: Responsible for details of interacting with GitHub
Releases API, downloading assets, and performing the upgrade of
individual assets.

We will make the responsibilities of these components more clear, such
that the UpgradeOrchestrator will be responsible for the high level
general steps of an upgrade, while the ProductUpgrader will contain
the logic for interacting with the different feeds and installing
packages from these feeds. In this model, we would have a
"GitHubReleasesUpgrader" and a "NuGetPackageUpgrader"

The UpgradeOrchestrator will delegate the actual upgrade logic to
individual ProductUpgraders. The existing upgrade logic for upgrading
from the public GitHub releases API will be one ProductUpgrader
implementation. We will implement another ProductUpgrader for the
upgrades from NuGet feeds.

- Checking for a new version
  - Delegate the logic of querying the feed to Upgrader
- Running prechecks
- Downloading new version
  - Download logic delegated to ProductUpgrader
- Unmounting all repositories
- Running installer
  - Installer logic delegated to ProductUpgrader
- Remounting repositories
- Deleting downloaded files
  - cleanup delegated to Upgrader

#### Upgrade with Alternative feed

When upgrading from NuGet package feed, the NuGet package will be
downloaded and the individual well know directories extracted. The
installers from these directories will then be run.

### Checking for new upgrades

ProductUpgrader currently contains the logic for determining when an
upgrade is available. It currently does this by checking if an
installer has been downloaded.

Now that we have to handle different feed sources, the installers will
need to follow a convention for us to read the version from, or we
might want to put the version infromation in a well known file. The
advantage of storing the version information in a file is that we can
update this file by querying the feed endpoints, and not necessarily
waiting to download the actual installer.

### TBD:

* Cross platform concerns?
