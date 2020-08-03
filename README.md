# ZubRobot
Simple C# robot for Zubr exchange

---
**Summary**
---

The robot simultaneously places orders into the order book and changes their price depending on what is happening on the market. At the first moment of work, the robot connects to the api-gate, checks that the authorization and all subscriptions are carried out correctly and sets:

BUY order with the volume specified in the configuration file at a price equal to (current best purchase price + current best sale price) / 2 - interest - shift * position;
SELL order with the volume specified in the configuration file at a price equal to (current best purchase price + current best sale price) / 2 + interest - shift * position.

In case of partial execution of an order, its volume changes to the set one at the next price change. In the case of market price changes, orders are moved to complete the formula above. Position calculation during work are based only on an execution report. Initial position can be chosen as received from an exchange as well as manually chosen in the config file.

---
**Installation**
--
After a Visual Studio build, the config file will be copied to the output directory, next to your executable file. It's name is config.json, before running the application, you have to specify the following in it:

- api endpoint - ZUBR exhange api endpoint, currently wss://uat.zubr.io/api/v1/w for test and wss://zubr.io/api/v1/ws for production
- api key - your api key
- api secret - your api secret
- Instrument code - instrument Id you want to trade
- Quote size - The quantity that the robot will send out the orders with
- Interest - Your expected interest for the price calculation
- Shift - Shifting prices in the calculation with this value
- Max position - If your position goes above this (or below in negative area), the robot will not place new orders until the position is decreased
- Initial position on the instrument - this is the position for the calculations if we want to override the position on the exhange from config
- Boolean flag to choose position from a config file or from exchange - if true, the robot will not query the exhange for position to use in price calculation, it will use the configured one
- Log Transport messages to exhange - Log messages sent and received from the exhange
- Log logical events - Log events happening in the robot's business logic
- The minimum tick size of the instrument - the robot will round the calculated prices to comply with the minimum tick size
