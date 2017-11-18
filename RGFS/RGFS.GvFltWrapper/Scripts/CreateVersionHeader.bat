mkdir %2\BuildOutput
mkdir %2\BuildOutput\GvLib.Managed

set comma_version_string=%1
set comma_version_string=%comma_version_string:.=,%

echo #define GVLIB_FILE_VERSION %comma_version_string% > %2\BuildOutput\GvLib.Managed\VersionHeader.h
echo #define GVLIB_FILE_VERSION_STRING "%1" >> %2\BuildOutput\GvLib.Managed\VersionHeader.h
echo #define GVLIB_PRODUCT_VERSION %comma_version_string% >> %2\BuildOutput\GvLib.Managed\VersionHeader.h
echo #define GVLIB_PRODUCT_VERSION_STRING "%1" >> %2\BuildOutput\GvLib.Managed\VersionHeader.h