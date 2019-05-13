. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

pkill -9 -l GVFS.FunctionalTests
pkill -9 -l git
pkill -9 -l gvfs
pkill -9 -l GVFS.Mount
pkill -9 -l prjfs-log

if [ -d ~/GVFS.FT ]; then
    rm -r ~/GVFS.FT
fi
