mkdir %2\BuildOutput
mkdir %2\BuildOutput\GvFlt

set comma_version_string=%1
set comma_version_string=%comma_version_string:.=,%

echo #define GVFLT_FILE_VERSION %comma_version_string% > %2\BuildOutput\GvFlt\VersionHeader.h
echo #define GVFLT_FILE_VERSION_STRING "%1" >> %2\BuildOutput\GvFlt\VersionHeader.h
echo #define GVFLT_PRODUCT_VERSION %comma_version_string% >> %2\BuildOutput\GvFlt\VersionHeader.h
echo #define GVFLT_PRODUCT_VERSION_STRING "%1" >> %2\BuildOutput\GvFlt\VersionHeader.h