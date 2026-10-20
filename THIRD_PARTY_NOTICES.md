# Third-Party Notices

This file summarizes third-party components and references used by qdisk.

## Rufus

- Project: Rufus, The Reliable USB Formatting Utility
- Upstream: https://github.com/pbatard/rufus
- License: GNU General Public License version 3 or later
- Copyright: Rufus contributors, including Pete Batard and upstream component authors

qdisk may incorporate or adapt Rufus-compatible boot media logic and artifacts under GPLv3-compatible terms. When such code or artifacts are included in distributed binaries, the corresponding source code for qdisk must be made available under GPLv3-or-later.

## UEFI:NTFS

- Project: UEFI:NTFS - Boot NTFS or exFAT partitions from UEFI
- Upstream: https://github.com/pbatard/uefi-ntfs (image distributed by Rufus at `res/uefi/uefi-ntfs.img`)
- License: GNU General Public License version 2 (UEFI:NTFS loader and ntfs-3g based NTFS drivers)
- Copyright: Pete Batard and contributors
- Usage: `assets/uefi-ntfs/uefi-ntfs.img` is written unmodified to a small auxiliary FAT partition of the USB
  drive for the UEFI:NTFS boot method. It is not linked into qdisk. Origin, commit and SHA-256 are
  recorded in `assets/uefi-ntfs/ORIGEM.txt`; source code is available at the upstream repositories above.

## DiscUtils

- Project: DiscUtils
- License: MIT
- Usage: ISO9660/UDF reading and file extraction.

## Microsoft Windows APIs and Tools

qdisk uses Windows APIs for removable drive enumeration, volume handling, formatting, raw device access, and safe media operations. If external Microsoft tools are used in a deployment, their redistribution and usage must follow the applicable Microsoft license terms.
