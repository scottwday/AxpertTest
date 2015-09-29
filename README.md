# AxpertTest
Simple command line utility to communicate with an Axpert inverter

# Usage
AxpertTest -p [COM Port] <-b [baud rate]> <-t [timeout ms]> command

- Use -p to set the com port (required)
- Use -b to set the baud (optional, default is 2400)
- Use -t to set the timeout in milliseconds (optional, default is 1000)

eg.
- AxpertTest -p COM3 QPIGS
- AxpertText -p COM3 -b 2400 -t 1000 QPIGS
