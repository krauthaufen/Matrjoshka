h1. Matrjoshka Routing

This Readme contains an installation manual as well as the manual for a quick test.

h2. Quick test

Log in onto Amazon EC2 service and start the instance "G1-T3-General".
Connect to the instance and run start <port>. 

Afterwards execute start.sh in the home folder of the Virtual box. This should just start Matrjoshka.exe <ip> <port> with the corresponding ip and port of the directory.

When the startup is finished you can open a browser (in the VM) and connect to localhost:1337.

There is the Webinterface where you can click "Build new Chain" to get a new Chain, or "Get Quote" to get a quote. Note: for the latter one you need to have a Chain established.


h2. Installation

Note if you use the VM you don't need to do any of these.

Clone this git repo.
Install Mono.
> sudo apt-get install mono
Install fsharp.
> sudo apt-get install fsharp
Import mozroot Certificates.
> mozroots --import --sync
Run build.sh.