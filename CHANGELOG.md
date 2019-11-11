# OPC-UA Extractor Changelog

1.0.0 2019-11-07
------------------
* Moved responsibility for handling non-finite datapoints to the pushers, resulting in a small change to the config scheme.
* Improved user feedback on common OPC-UA errors
* Add support for integer/bool/string datatypes to influx pusher
* Some added metrics
* Updated dependencies
* Numerous bugfixes

0.10.0 2019-10-21
------------------
* Node discovery using either periodic restarts or audit events
* Improved performance on fetching latest timestamp from CDF

0.9.0  2019-10-21
------------------
* Update CogniteSdk to public version
* Improvements to restarting and re-browsing
* Fix limit of pushes to CDF, and make larger data batches work

0.8.1  2019-10-07
------------------
* Config option for earliest history-read timestamp

0.8.0  2019-10-07
------------------
* Add support for reading events from OPC-UA and pushing to events in CDF.
* Update to .NET Core 3.0, trim and optimize binaries, and deploy as a single platform specific executable

0.7.0  2019-10-03
------------------
* Configurable chunk sizes to CDF and OPC UA server.
* Disable history synchronization with Source.History = false.

0.6.0  2019-10-01
------------------
* Fixed: reading RootNode on Prediktor.
* Added: NonFiniteReplacement config.

0.5.0  2019-09-23
------------------
* ...