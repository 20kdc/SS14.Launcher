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
	tags.append(tag.strip())
tags.sort(key=versionkey)

def check(args):
	res = subprocess.run(args)
	if res.returncode != 0:
		raise Exception("Invalid")

print("setting up patches...")

for version in patches:
	patch = patches[version]
	print(" " + patch["patch"])
	check(["./setup-patch", patch["base"], patch["patch"]])
	ref = open(patch["patch"] + ".commit", "r").readline().strip()
	print(" " + ref)
	# This is something that "git cherry-pick" can use.
	# Git is better at compensating for changes, since it sees the common base.
	patch["ref"] = ref

print("applying to tags...")

def set_patch_commit(patch):
	commit_file = open("current.commit", "w")
	commit_file.write(patch["ref"])
	commit_file.close()

active = False

for tag in tags:
	if tag in patches:
		active = True
		set_patch_commit(patches[tag])
	if active:
		print(tag)
		check(["./cross-pick", tag])
