#!/bin/bash

mono Prebuild.exe /target vs2010

if [[ $1 == build ]] ; then
    xbuild OpenSim.sln
else
    echo Next, run the 'xbuild' command to compile
    echo
fi
