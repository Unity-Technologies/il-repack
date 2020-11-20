PLEASE NOTE
------------

This is NOT the original ILRepack distribution which resides on gihub at
https://github.com/gluck/il-repack

This version of ILRepack was forked FROM the original distribution
https://github.com/gluck/il-repack TO https://github.com/Unity-Technologies/il-repack,
which integrates from Revision f5831d9097356dc2131e37e22039b278262032e6
(cecil-unfork branch)

We use this fork because since it includes Cecil 0.10.1, which allows ILRepack
to support Portable PDBs.  When PR https://github.com/gluck/il-repack/pull/236
lands in the mainline, then it should be possible for us to migrate back to the
stock distribution from NuGet. At this time, this seems very unlikely as the
mainline development stalled for many months.  

We are using our internal yamato build system in order to build, sign and
publish this to stevedore package manager.  Signing the executable prevents
anti-virus heuristics to detect false positive viruses. To create a new build,
simply create a new version tag.  You will currently find the package in 
stevedore public.