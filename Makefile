# hey, emacs! this is a -*- makefile -*-
#
# OpenSim makefile
#

XBUILD    = xbuild

all: prebuild
	# @export PATH=/usr/local/bin:$(PATH)
	${XBUILD}
	find OpenSim -name \*.mdb -exec cp {} bin \; 

release: prebuild
	${XBUILD} /p:Configuration=Release
	find OpenSim -name \*.mdb -exec cp {} bin \;

prebuild:
	./runprebuild.sh

clean:
	# @export PATH=/usr/local/bin:$(PATH)
	-${XBUILD} /target:clean

tags:
	find OpenSim -name \*\.cs | xargs etags 

cscope-tags:
	find OpenSim -name \*\.cs -fprint cscope.files
	cscope -b

include $(wildcard Makefile.local)
