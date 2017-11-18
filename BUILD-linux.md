To enable access to the USB devices, you will need to add a new udev rule:

Content:
--------
# /etc/udev/rules.d/50-sunxi-fel.rules
#SUBSYSTEMS=="usb", ATTRS{idVendor}=="1f3a", ATTRS{idProduct}=="efe8", MODE:="0666"
SUBSYSTEM=="usb", ATTRS{idVendor}=="1f3a", MODE="0666"
SUBSYSTEM=="usb_device", ATTRS{idVendor}=="1f3a", MODE="0666"

(This enables access to all devices with vendor-id "1f3a")

Reload udev-rules:
------------------
$ sudo udevadm control --reload-rules && sudo udevadm trigger
