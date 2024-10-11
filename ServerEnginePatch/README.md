# 'NewKey' Server Engine Patch

This is a mechanism to help maintain the server engine patch used to allow key-based authentication on otherwise stock RobustToolbox.

Basically, the engine patch is divided into parts, presently:

* "Platform": Adds the JWT dependency.
* "InfraStatusHost": Adds the NewKey support to StatusHost & infrastructure in NetManager.
* "ServerAuth": Integrates NewKey support into NetManager.ServerAuth.

Ideally, these patches should not touch the same files as each other.

It's probably best to separate any new "things that we have to call" into separate patches (i.e. the logger thunk for `0.85.1.4` may end up in a separate patch). This is because the more variants there need to be of a patch, the worse it is when the interface changes.

The patches are managed using the `update` script. This script assumes a `RobustToolbox` symlink exists. The script, with `driver.py`:

* Ensures that all `origin`'s tags are available.
* Using `patches.json`, creates `newkey-auto-` tags for each version for which a patch definition can be found.

To make some use of Git's merge strategies, patchsets are specified in the form of a base ref and a list of patch files. The idea here is to write the patch against the base ref, the script applies it against that ref, and Git's conflict resolution figures out the rest when translating it over to whatever it's being applied on. There's probably an even better way of doing this, but I'm not so sure how well it'd work with the switching bases, and it'd be complicated to actually use. So instead the limitations with this approach are resolved with careful management of the patches themselves.