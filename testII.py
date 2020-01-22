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
import tkinter as Tkinter
from PIL import Image, ImageTk
import numpy
#from scipy.misc import toimage
import sys, os, time


RAW_WIDTH = 342
RAW_HEIGHT = 260
NUM_DATA_EXPECTED = 2 * RAW_WIDTH * RAW_HEIGHT



# ++++++++++++++++++++++++++++++++++++++++++++


print("hello")
dev = usb.core.find(find_all=True)
# loop through devices, printing vendor and product ids in decimal and hex
i=0
for cfg in dev:
#   print (i)
  i = i+1
#   print(str(cfg))
#   print(str(cfg.idVendor))
  print('Hexadecimal VendorID=' + hex(cfg.idVendor) + ' & ProductID=' + hex(cfg.idProduct) + '\n\n')

# dev = usb.core.find(find_all=True)
# for d in dev:
#   print (usb.util.get_string(d,128,d.iManufacturer))
#   print (usb.util.get_string(d,128,d.iProduct))
#   print (d.idProduct,d.idVendor)


# find our Seek Thermal device  289d:0010
dev = usb.core.find(idVendor=0x289d, idProduct=0x0011)
if not dev: raise ValueError('Device not found')

# print (str(dev))

def send_msg(bmRequestType, bRequest, wValue=0, wIndex=0, data_or_wLength=None, timeout=100):
    assert (dev.ctrl_transfer(bmRequestType, bRequest, wValue, wIndex, data_or_wLength, timeout) == len(data_or_wLength))

# alias method to make code easier to read
receive_msg = dev.ctrl_transfer

def deinit():
    '''Deinit the device'''
    msg = '\x00\x00'
    for i in range(3):
        send_msg(0x41, 0x3C, 0, 0, msg)


# set the active configuration. With no arguments, the first configuration will be the active one
dev.set_configuration()

# get an endpoint instance
cfg = dev.get_active_configuration()
intf = cfg[(0,0)]

custom_match = lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_OUT
ep = usb.util.find_descriptor(intf, custom_match=custom_match)   # match the first OUT endpoint
assert ep is not None

# Setup device
try:
    msg = '\x01'
    send_msg(0x41, 0x54, 0, 0, msg)
except Exception as e:
    deinit()
    msg = '\x01'
    send_msg(0x41, 0x54, 0, 0, msg)

#  Some day we will figure out what all this init stuff is and
#  what the returned values mean.

# The original initialization script
def original_init():
    send_msg(0x41, 0x3C, 0, 0, '\x00\x00')
    ret1 = receive_msg(0xC1, 0x4E, 0, 0, 4)
    #print ret1
    ret2 = receive_msg(0xC1, 0x36, 0, 0, 12)
    #print ret2

    send_msg(0x41, 0x56, 0, 0, '\x20\x00\x30\x00\x00\x00')
    ret3 = receive_msg(0xC1, 0x58, 0, 0, 0x40)
    #print ret3

    send_msg(0x41, 0x56, 0, 0, '\x20\x00\x50\x00\x00\x00')
    ret4 = receive_msg(0xC1, 0x58, 0, 0, 0x40)
    #print ret4

    send_msg(0x41, 0x56, 0, 0, '\x0C\x00\x70\x00\x00\x00')
    ret5 = receive_msg(0xC1, 0x58, 0, 0, 0x18)
    #print ret5

    send_msg(0x41, 0x56, 0, 0, '\x06\x00\x08\x00\x00\x00')
    ret6 = receive_msg(0xC1, 0x58, 0, 0, 0x0C)
    #print ret6

    send_msg(0x41, 0x3E, 0, 0, '\x08\x00')
    ret7 = receive_msg(0xC1, 0x3D, 0, 0, 2)
    #print ret7

    send_msg(0x41, 0x3E, 0, 0, '\x08\x00')
    send_msg(0x41, 0x3C, 0, 0, '\x01\x00')
    ret8 = receive_msg(0xC1, 0x3D, 0, 0, 2)
    #print ret8

def new_init():
    # Set Operation Mode
    send_msg(0x41, 0x3C, 0, 0, '\x00\x00')

    # SET_FACTORY_SETTINGS_FEATURES
    send_msg(0x41, 0x56, 0, 0, '\x06\x00\x08\x00\x00\x00')

    # SET_FIRMWARE_INFO_FEATURES
    send_msg(0x41, 0x55, 0, 0, '\x17\x00')

    # SET_FACTORY_SETTINGS_FEATURES
    send_msg(0x41, 0x56, 0, 0, '\x01\x00\x00\x06\x00\x00')

    for i in range(10):
        for j in range(0,256,32):
            send_msg(0x41, 0x56, 0, 0, b"\x20\x00"+bytes([j,i])+b"\x00\x00")

    # SET_FIRMWARE_INFO_FEATURES
    send_msg(0x41, 0x55, 0, 0, '\x15\x00')

    # SET_IMAGE_PROCESSING_MODE
    send_msg(0x41, 0x3E, 0, 0, '\x08\x00')

    # SET_OPERATION_MODE
    send_msg(0x41, 0x3C, 0, 0, '\x01\x00')

# original_init()
new_init()

im2arrF = None
def get_image():
    # Send read frame request (START_GET_IMAGE_TRANSFER command)
    send_msg(0x41, 0x53, 0, 0, '\x58\x5b\x01\x00')
    
    # Read Data
    print("reading data")
    try:
        ret9  = dev.read(0x81, 13680, 1000)

        remaining = NUM_DATA_EXPECTED - len(ret9)

        # Loop through to get the rest of the data
        while remaining > 512:
            ret9 += dev.read(0x81, 13680, 1000)
            remaining = NUM_DATA_EXPECTED - len(ret9)
        
    except usb.USBError as e:
        print("exiting from usb.USBError as" + str(e))
        sys.exit()


    #  Let's see what type of frame it is
    #  1 is a Normal frame, 3 is a Calibration frame
    #  6 may be a pre-calibration frame
    #  5, 10 other... who knows.
    status = ret9[20]


    print("status: " + str(status))
    #print ('%5d'*21 ) % tuple([ret9[x] for x in (range21)])

    print("ret9: " + str(type(ret9)))

    print("red9 len: " + str(len(ret9)))

    
    if len(ret9) == RAW_HEIGHT*RAW_WIDTH*2:
        return numpy.frombuffer(ret9,dtype=numpy.uint16).reshape(RAW_HEIGHT,RAW_WIDTH)

    else:
        return None

try:
    for i in range(1):
        img = list(get_image())
    print(img)
    

except usb.USBError as e:
    pass

dev.reset()
usb.util.dispose_resources(dev)