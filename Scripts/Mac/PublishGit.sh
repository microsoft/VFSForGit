. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

GITPATH=$1
INSTALLER=$(basename $GITPATH)

GITPUBLISH=$VFS_OUTPUTDIR/Git
if [[ ! -d $GITPUBLISH ]] ; then
  mkdir $GITPUBLISH
fi

find $GITPUBLISH -type f ! -name $INSTALLER -delete

if [[ ! -e $GITPUBLISH/$INSTALLER ]] ; then
  cp $GITPATH $GITPUBLISH
fi
