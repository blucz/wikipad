#!/bin/bash
#
# deploy app build to its run location. run this after doing a build.
#
# you need to run this as root.
#

INSTALL_PATH=/opt/wikipad

suffix=`date "+%Y-%m-%d_%H:%M:%S"`
datedir="${INSTALL_PATH}_${suffix}"
tmplink="${INSTALL_PATH}_tmp"

# deploy to a dated directory
echo "Deploying files"
mkdir $datedir || exit 1
cp spawnhelper_autorestart $datedir || exit 2
cp bin/release/*           $datedir || exit 2
cp -a static               $datedir || exit 6

# copy init.d script
echo "Copying new /etc/init.d/wikipad"
cp wikipad /etc/init.d

# down service
echo "Stopping Wikipad"
/etc/init.d/wikipad stop
# kill pid file in case service had crashed
rm -f /var/run/wikipad.pid

echo "Updating symlink to current version"
# atomically replace symlink
ln -s $datedir $tmplink || exit 3
mv -Tf $tmplink $INSTALL_PATH || exit 4

echo "Starting Wikipad"
# up service
/etc/init.d/wikipad start || exit 5
