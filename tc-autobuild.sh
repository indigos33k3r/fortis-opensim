#!/bin/bash

mono Prebuild.exe /target vs2010

unset makebuild
unset makedist

while [ "$1" != "" ]; do
    case $1 in
	build )       makebuild=yes
                      ;;
	dist )        makedist=yes
                      ;;
    esac
    shift
done

if [ "$makebuild" = "yes" ]; then
    xbuild /t:Rebuild OpenSim.sln
    res=$?

    if [ "$res" != "0" ]; then
	exit $res
    fi

    if [ "$makedist" = "yes" ]; then
	cd bin
	mv Debug fortis-opensim-autobuild
	tar cjf fortis-opensim-autobuild.tar.bz2 fortis-opensim-autobuild
	rm -rf fortis-opensim-autobuild
	cd ..
    fi
fi
