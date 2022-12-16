# WSL IPv6 packet enabler

IPv6 on Windows and WSL has been a bane of my existence for years. When working 
on a project that specifically required IPv6, I realized I'd have to figure out
what was going on. Thus this little program was born.

## Requirements

There are several requirements to use this.

### Bridge mode

WSL must be running in bridge mode. For example, here is my `/mnt/c/Users/lande/.wslconfig` file:

```ini
[wsl2]
networkingMode=bridged #<--
vmSwitch=wsl #<--
dhcp=true #<--
ipv6=true #<--
macAddress=08:6a:c5:51:eb:bf #<--

memory=4GB
processors=2
localhostforwarding=true #<--
debugConsole=false
```

The "vmSwitch" is a normal bridged network interface set in hyper-v management console.

I then set up my `/etc/resolv.conf`.

## PCAP

I haven't installed this on a fresh computer, but it may required to have winpcap (at least wireshark) installed.

## Why doesn't IPv6 work in WSL??

After much investigation and fiddly bits with wireshark, I came to realize that IPV6 packets weren't 
being forwarded to WSL correctly. I don't know why, but for whatever reason no one at Microsoft is 
smart enough or cares enough to fix what is probably a really simple bug.

Thus, this incredibly simple program was born.

## What this program does

1. Listens for ipv6 packets sent to the Windows host with the wrong mac address.
2. Resends them with the correct mac address.
3. And that is literally it.

## Using the program

This is a bare bones implementation at the moment. As it seems that Microsoft will probably never
fix this, you can expect this to get a lot better fairly quickly.

### Get the program

You can either download the source and build it yourself or download the binary from releases.

## Select your interface

![Screenshot][[program.png]]

In the interface, select the device that your computer is connected to.

Mine was something like "Hyper-V Virtual Ethernet Adapter #2 [086AC551EBBD]". I don't know an easy way to figure this out. 
If you know a way open an issue or PR!

Then enter the mac address you entered into you `.wslconfig`. Finally, push "Start".

Now, in wsl run `ping ipv6.google.com` and within a few seconds, you should see something.

## Why does this work?

Initial connections take a few packets going to the wrong place before eventually going to the right place (and this 
program will handle that). You **don't** need to keep this running all the time! You only need it to "train" your network 
to send packets to the right place. Once you've done that, IPv6 will continue running just fine for quite awhile.
