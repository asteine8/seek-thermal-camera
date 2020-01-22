# You will need to have python 2.7 (3+ may work)
# and PyUSB 1.0
# and PIL 1.1.6 or better
# and numpy
# and scipy
# and ImageMagick

# Many thanks to the folks at eevblog, especially (in no particular order) 
#   miguelvp, marshallh, mikeselectricstuff, sgstair and many others
#     for the inspiration to figure this out
# This is not a finished product and you can use it if you like. Don't be
# surprised if there are bugs as I am NOT a programmer..... ;>))


## https://github.com/sgstair/winusbdotnet/blob/master/UsbDevices/SeekThermal.cs

import usb.core
import usb.util
# import tkinter as Tkinter
# from PIL import Image, ImageTk
# import numpy
# #from scipy.misc import toimage
import sys, os, time


print("hello")
dev = usb.core.find(find_all=True)
# loop through devices, printing vendor and product ids in decimal and hex
print (str(dev))
i=0
for cfg in dev:
  print (i)
  i = i+1
  print(str(cfg))
  print(str(cfg.idVendor))
  print('Hexadecimal VendorID=' + hex(cfg.idVendor) + ' & ProductID=' + hex(cfg.idProduct) + '\n\n')

# dev = usb.core.find(find_all=True)
# for d in dev:
#   print (usb.util.get_string(d,128,d.iManufacturer))
#   print (usb.util.get_string(d,128,d.iProduct))
#   print (d.idProduct,d.idVendor)


# find our Seek Thermal device  289d:0010
dev = usb.core.find(idVendor=0x289d, idProduct=0x0011)
if not dev: raise ValueError('Device not found')