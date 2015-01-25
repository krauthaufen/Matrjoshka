# Matrjoshka Routing

This Readme contains an installation manual as well as the manual for a quick test.

## Quick test

Log in onto Amazon EC2 service and start the instance "G1-T3-Directory". This is an instance of the "G1-T3-Mono" AMI with the Security Group "G1-T3-General".

Connect to the instance and run 
> start directory \<port\>


Afterwards execute 
> start \<ip\> \<port\>

in the home folder of the Virtual box. This should just start Matrjoshka.exe \<ip\> \<port\> with the corresponding ip and port of the directory.

For the test we try to keep the instance with the IP "54.154.222.15" up. As the Port is restricted by the security group we recommend using the port "9999".

When the startup is finished you can open a browser (in the VM) and connect to localhost:1337.

There is the Webinterface where you can click "Build new Chain" to get a new Chain, or "Get Quote" to get a quote. Note: for the latter one you need to have a Chain established.

To stop any component just connect to it and write
> !shutdown

On the directory this terminates the Service as well as the Chain Nodes. (Note: for simplicity the Directory also starts/stops the service... Normally this wouldnt be appropriate).

## Installation

Note if you use the VM you don't need to do any of these.

Clone this git repo.
Install Mono.
> sudo apt-get install mono

Install fsharp.
> sudo apt-get install fsharp

Import mozroot Certificates

> mozroots --import --sync

Run build.sh.