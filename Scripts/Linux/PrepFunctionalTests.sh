#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

# Install GVFS-aware Git (that was published by the build script)
GITPUBLISH=$VFS_OUTPUTDIR/Git
if [[ ! -d $GITPUBLISH ]]; then
    echo "GVFS-aware Git package not found. Run BuildGVFSForLinux.sh and try again"
    exit 1
fi
GITPKG="$(find $GITPUBLISH -type f -name *.deb)" || exit 1
sudo apt-get install -y "$GITPKG" || exit 1

# Run ldconfig to ensure libprojfs and libattr are cached for the linker
sudo ldconfig
