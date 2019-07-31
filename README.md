# opcua-extractor-net
[![build](https://webhooks.dev.cognite.ai/build/buildStatus/icon?job=github-builds/opcua-extractor-net/master)](https://jenkins.cognite.ai/job/github-builds/job/opcua-extractor-net/job/master/)
[![codecov](https://codecov.io/gh/cognitedata/opcua-extractor-net/branch/master/graph/badge.svg?token=SS8CBL93bW)](https://codecov.io/gh/cognitedata/opcua-extractor-net)

OPC-UA extractor using libraries from OPCFoundation see [here](https://github.com/OPCFoundation/UA-.NETStandard)

## How to use

### Building
Install .net core 2.2 from [here](https://dotnet.microsoft.com/download).

Add artifactory as a nuget source as outlined [here](https://cognitedata.atlassian.net/wiki/spaces/IDE/pages/711884992/Migrating+to+Artifactory)
then simply run `dotnet restore` to install dependencies.

To run:
`dotnet run [optional config directory]`. If no config directory is specified, [application dir]/config is used.
See the [example configuration](config/config.example.yml) for a template.

### Using Docker
Simply download and run the latest build from [here](https://console.cloud.google.com/gcr/images/cognitedata/EU/opcua-extractor-net?gcrImageListsize=30)

Config, both opcua config `opc.ua.extractor.Config.xml` and `config.yml` are located in a volume /config. Example:

`docker run -v "$(pwd)/config:/config" eu.gcr.io/cognitedata/opcua-extractor-net:tag`

which would run the build tagged with `tag` using config stored in `current_dir/config`.

### Configuration
The config file should contain description of most config options, a few are notable
 - `GlobalPrefix` and the `NSMaps` category are used to create ExternalIds, and if these are changed after the extractor
 has already been run, then new assets and timeseries will be created. The externalId will be on the form
 `[GlobalPrefix].[Mapped or full namespace]:[Identifiertype and identifier]`. If GlobalPrefix is chosen to be unique within the project then the ExternalId should remain unique.
 Note that there is an exception to this if the IDs are longer than 128 characters in total. externalId in CDF is limited to 256 characters, but NodeIds may have identifiers of up to 4096 characters. To fit with CDF we cut off everything after the first 256 characters in the final ID. This means that if the namespaces are long enough, we potentially only get 180 - 200 characters from the identifier itself. If this creates duplicates then the duplicates will be ignored.
 - `RootAssetId`, `RootNamespaceId` and `RootNodeId` are used to designate the root node and root asset, these are mapped such
 that the RootAsset and RootNode will be in the same place in the final hierarchy. In theory, multiple extractors could
 run in parallel against different top level assets/nodes.
 - Buffering is done by setting `BufferOnFailure` to true and specifying a `BufferFile`. If this is done, the extractor
 will write non-historizing datapoints to a binary file when the connection to CDF is down.

## Development
You will need .net core 2.2, and to set up artifactory as mentioned above. Then simply run `dotnet build` to compile,
or `dotnet run` to compile and run. Make sure that the config file is moved to the build directory, either by specifying
in the solution or by moving it manually.

For testing metrics, a good solution is the prom-stack found [here](https://github.com/evnsio/prom-stack)

### Testing
There is a test script under Testing/test.sh. To run the tests locally, you should first start the python test servers, using `Test/startservers.sh`. There are two test servers currently.

 - Basicserver, server-test1.py, is a small server with two historizing nodes and a few objects. 4840
 - Fullserver, server-test2.py has around 2000 variables and a 30 layer deep tree of 150 objects. 4841

To add more servers just add them to the servers folder, then edit the startservers.sh script to start the new server in the background. Make sure to use a different port.
 
 
