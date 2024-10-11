#!/usr/bin/env python3

import json
import subprocess

def versionkey(a):
	assert a[0] == "v"
	vrem = a[1:]
	parts = vrem.split(".")
	assert len(parts) <= 4
	key = 0
	for i in range(4):
		key *= 10000
		if i >= len(parts):
			continue
		key += int(parts[i])
	return key

patches = json.load(open("patches.json", "r"))
tags_unfixed = open("tags", "r").readlines()
tags = []
for tag in tags_unfixed:
	if tag.strip().endswith("-pvsdebug"):
		continue
	tags.append(tag.strip())
tags.sort(key=versionkey)

def check(args):
	print(args)
	res = subprocess.run(args)
	if res.returncode != 0:
		raise Exception("Invalid")

def get_current_commit():
	return open("current.commit", "r").readline().strip()

def set_current_commit(value):
	commit_file = open("current.commit", "w")
	commit_file.write(value)
	commit_file.close()

print("setting up patches...")

for version in patches:
	patch = patches[version]
	if patch is None:
		continue
	print(" " + version)
	ref = patch["base"]
	for v in patch["list"]:
		check(["./setup-patch", ref, v])
		ref = get_current_commit()
	print(" " + ref)
	# This is something that "git cherry-pick" can use.
	# Git is better at compensating for changes, since it sees the common base.
	patch["range"] = patch["base"] + ".." + ref

print("applying to tags...")

patch_range = None

for tag in tags:
	if tag in patches:
		if patches[tag] is None:
			patch_range = None
		else:
			patch_range = patches[tag]["range"]
	if not (patch_range is None):
		print(tag)
		check(["./cross-pick", tag, patch_range, "newkey-auto-" + tag])
		patch_range = tag + ".." + get_current_commit()
