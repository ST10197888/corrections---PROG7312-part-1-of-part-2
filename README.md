# Smart-X IoT Telemetry System 



### Smart-X is a WPF desktop application made up of three parts that work together:

* **SmartX.API** – An ASP.NET Core Web API that receives sensor readings, detects anomalies, stores alerts, and handles user-created sensors.
* **SmartX.Core** – The shared domain logic for telemetry packets, power-grid metrics, and anomaly detection rules.                                         
* **SmartX.UI** – A WPF desktop dashboard application that talks to the API and visualizes everything.

---

## Running the APP

Before you use the application, ensure the following:
* Ensure the **SmartX.API** project is set as the startup project. Then run the project `cd SmartX.API` then `dotnet run` within the nuget package manager console. 
  <img width="979" height="641" alt="Screenshot 2026-09-14 084806" src="https://github.com/user-attachments/assets/5bd96794-b96c-4d97-8351-796b3ee19510" />

* Ensure the **SmartX.API** project is running and "now listening at http://localhost:5001" is displayed
<img width="1919" height="638" alt="cmd output" src="https://github.com/user-attachments/assets/e7daf74b-dd4a-4c9c-9e40-a6d4c4b781bd" />

---

## API-Only Tests

To register a new test Sensor using Postman, use the following configurations:

The `{{baseUrl}}` is set to `http://localhost:5001`. The current status of the project is dependent on `http://localhost:5001` and not `https://localhost:7001` (unlike the settings inside `launchSettings.json`).

> These tests work well with the Postman application within a new environment, but only under `http://localhost:5001` unless values are changed within the `launchSettings.json` file.

Create an Environment, name it `Smart.X.api`, and add a new variable: `baseUrl = http://localhost:5001` 

### Test 1 - Register a new sensor

* **Method type:** `POST`
* **URL:** `{{baseUrl}}/api/telemetry/register-sensor` 
* **Headers:** `Content-Type: application/json`
* **Body:** Set to `raw`, then `JSON`, and paste:

```json
{
  "macAddress": "00:1A:2B:3C:4D:5E",
  "locationZone": "Zone 1",
  "category": "Environmental",
  "description": "Soil moisture sensor"
}
```

#### Output when successful:
<img width="1919" height="1079" alt="Screenshot 2026-09-14 074611" src="https://github.com/user-attachments/assets/69c291c1-08b5-4d89-b2fa-68336cb4dc26" />


This will simulate a new sensor being registered with the API. The API will return a success message with the details of the newly registered sensor.

---

### Test 2 - Ingest a normal telemetry reading

> This test relies on the first test being successful and the sensor being registered with the API.

* **Method type:** `POST`
* **URL:** `{{baseUrl}}/api/telemetry/ingest` 
* **Headers:** `Content-Type: application/json`
* **Body:** Set to `raw`, then `JSON`, and paste:

```json
{
  "macAddress": "00:1A:2B:3C:4D:5E",
  "locationZone": "Zone 1",
  "category": "Power Consumption",
  "sensorValue": 5000,
  "timestamp": "2026-09-14T10:30:00"
}
```

#### Output when successful:
<img width="1918" height="1078" alt="Screenshot 2026-09-14 075354" src="https://github.com/user-attachments/assets/dff6227d-4ea2-4c8d-912b-2e5d9a82c2ad" />

This test will simulate a telemetry reading being sent to the API from a registered sensor. The API will return a success message with the details of the ingested telemetry reading.

---

### Test 3 - Ingest an anomalous telemetry reading

The anomaly detector flags power readings over 9500. Sending a value above that will trigger a high-severity alert.

* **Method type:** `POST`
* **URL:** `{{baseUrl}}/api/telemetry/ingest` 
* **Headers:** `Content-Type: application/json`
* **Body:** Set to `raw`, then `JSON`, and paste:
  
```json
{
  "macAddress": "00:1A:2B:3C:4D:5E",
  "locationZone": "Zone 1",
  "category": "Power Consumption",
  "sensorValue": 9850,
  "timestamp": "2026-09-14T10:35:00"
}
```

#### Output when successful:
<img width="1919" height="1079" alt="Screenshot 2026-09-14 075749" src="https://github.com/user-attachments/assets/9c3e8dc2-1071-4c17-b915-f6b3037faf95" />

This test will simulate a telemetry reading being sent to the API from a registered sensor. The API will return a success message with the details of the ingested telemetry reading.

---

### Test 4 - Get all Sensors in the system at current session

> **Memory Warning:** The API and WPF data is stored in-memory and will be lost when the application is closed. This test will return all sensors that have been registered during the current session.

* **Method type:** `GET`
* **URL:** `{{baseUrl}}/api/telemetry/sensors` 

#### In a similar way, to get all alerts in the system at the current session, use the following:
	
* **Method type:** `GET`
* **URL:** `{{baseUrl}}/api/telemetry/alerts` 


This test will fetch all the sensors or alerts that have been registered during the current session.

#### Output when successful:
<img width="1919" height="1079" alt="Screenshot 2026-09-14 080425" src="https://github.com/user-attachments/assets/39e978b0-87b9-4b05-a999-1266b6f0124c" />
---

### Test 5 - Get a specific Sensor's History

> The API and WPF data is stored in-memory and will be lost when the application is closed.

* **Method type:** `GET`
* **URL:** `{{baseUrl}}/api/telemetry/history/00:1A:2B:3C:4D:5E` 

> This URL uses the example created in the first test. Change the MAC address parameter to match the specific sensor you want to get history for.

#### Output when successful:
<img width="1919" height="1079" alt="Screenshot 2026-09-14 080631" src="https://github.com/user-attachments/assets/3d723d70-492d-4aea-be6c-f2a486312767" />

This test will retrieve the historical telemetry readings for a specific sensor.

---
# Running the Application

## Running the Dashboard (SmartX.UI WPF App)

>  Ensure `SmartX.UI` and `SmartX.API` projects are configured as multiple startup projects within the solution properties, then run the application. The dashboard will connect to the API and display the registered sensors, telemetry readings, and alerts in real-time.

<img width="1038" height="649" alt="Screenshot 2026-09-14 081334" src="https://github.com/user-attachments/assets/321a1ec5-ec4c-4199-a36a-1a34f03899cd" />

> Make sure the API is already running, as the dashboard heavily depends on it. The dashboard will still launch if the API is not running, but it will operate with limited functionality. In that fallback mode, it displays local WPF-side data generation rather than real-time data driven by the API.

The main window (Shell) loads with the **Landing Page** by default, featuring a navigation bar across the top containing: *Dashboard*, *View Telemetry*, *Real-Time Command Stream*, and *Network Topology*.

> *Real-Time Command Stream* and *Network Topology* are not yet implemented and will be added in future updates.

### Navigation Bar
<img width="1228" height="360" alt="Screenshot 2026-09-14 085232" src="https://github.com/user-attachments/assets/8880c293-a984-4df9-a445-73bfcddd32e9" />

---

## Landing Page / Dashboard: 
* The dashboard page displays the registered sensors, telemetry readings, and alerts in real-time. 
* The dashboard updates automatically as new data is received from the API.
 
The landing page shows three side-by-side panels:
* Shared telemetry readings (latest 15 readings) UI - works when API online / offline and API - only when API online
<br>
Two side-by-side alert and resolved panels:
* Active alerts (latest 15 readings) - only when API online
* Solved alerts (latest 15 readings) - only when API online


<img width="1919" height="1078" alt="Screenshot 2026-09-14 090501" src="https://github.com/user-attachments/assets/caa520bd-6995-4861-9c7a-91fa2ee77354" />
Dashboard screenshot 1 - all connected and has data

<br><br>

<img width="1919" height="1079" alt="Screenshot 2026-09-14 090123" src="https://github.com/user-attachments/assets/daabfb0a-3445-4168-932f-39643ca00aa3" />
Dashboard screenshot 2 - API not connected, showing WPF-side data only

---

## View Telemetry: 
* The telemetry page displays the historical telemetry readings for a specific alert sensor. 
* The user can select a sensor from the list of registered sensors and view its historical telemetry readings if that specific sensor has had an anomaly detected, generated an alert by the API, and has been resolved.
 
> The user is able to view the resolved alert's historical telemetry readings, including the timestamp it was updated at.

<img width="1919" height="1079" alt="Screenshot 2026-09-14 090513" src="https://github.com/user-attachments/assets/3db51aeb-0569-4f9f-9da8-b23abb0a4095" />
API connected

<br><br>

<img width="1919" height="1079" alt="Screenshot 2026-09-14 090119" src="https://github.com/user-attachments/assets/53005267-56b2-4120-866b-ccee85dbe985" />
API not connected

<br><br>

<img width="312" height="499" alt="image" src="https://github.com/user-attachments/assets/776633b4-e61c-4518-b6e5-0b2104eb4683" />
System metrics - a live performance / health readout for the local data store in the WPF APP.


---

## Real-Time Command Stream
Not required as of now - will be built in the near future

<img width="1918" height="1079" alt="Screenshot 2026-09-14 085830" src="https://github.com/user-attachments/assets/d0d48c9c-a39f-4e2a-9b76-dfa173dc5647" />

---

## Network Topology
Not required as of now - will be built in the near future

<img width="1919" height="1079" alt="Screenshot 2026-09-14 085834" src="https://github.com/user-attachments/assets/458faaad-ee19-4087-bb09-9954c7bf2124" />

---

# More About the App

### Alerts & Anomaly Detection
>when an anomaly is detected it becomes an alert then it is either resolved by operator or auto-resolved till resolved then finaly moves to the resolved tracker removing the alert in the process.

The API automatically flags anomalies as telemetry when ingested. like a power-consumption reading above a threshold are flagged as `high severity`.
These are then issued into the alert tracker which then displays it to the user with it's viewable details and editable "alert" and "solved alert" states though a solve button

<img width="1641" height="846" alt="Screenshot 2026-09-14 091845" src="https://github.com/user-attachments/assets/10487447-9076-4c87-94ad-95e9ab828796" />
detected anomaly number 9742

<br><br>

<img width="1919" height="1077" alt="Screenshot 2026-09-14 091858" src="https://github.com/user-attachments/assets/c3b32797-08d2-4d10-9a29-f39b62d992e5" />
anomaly 9742 details

<br><br>

<img width="1918" height="1078" alt="Screenshot 2026-09-14 091921" src="https://github.com/user-attachments/assets/a12aace6-27f7-47f0-bae9-4e1874689b42" />
detected anomaly number 9742 now alert 9742

<br><br>

<img width="1096" height="192" alt="Screenshot 2026-09-14 091714" src="https://github.com/user-attachments/assets/112d5007-9f2f-43a5-99c0-73ef9769469f" />                            
detected alerts

<br><br>

<img width="1919" height="1079" alt="Screenshot 2026-09-14 091709" src="https://github.com/user-attachments/assets/276c6028-39d7-4c45-bbfc-e13eb99d7eb2" />
Alert details

### Alert Solving

The API automatically solves anomaly alerts as telemetry when ingested after a certain amount of time passes. 
This mimics an operator resolving an alert over time, rather than the API resolving it instantly. Periodically, the API sweeps and auto-resolves any alerts that have been active long enough, as shown below with the Environmental reading 28.69 alert..

<img width="1919" height="1079" alt="Screenshot 2026-09-14 093556" src="https://github.com/user-attachments/assets/8f46cccd-573f-47b8-bb45-05645bd46cd1" />
anomaly 28.69 details

<br><br>

<img width="1919" height="1079" alt="Screenshot 2026-09-14 093336" src="https://github.com/user-attachments/assets/7c8b0642-d9a1-4ece-989e-48efbc1786b0" />
detected anomaly 28.69 before autosolved

<br><br>

<img width="1919" height="1078" alt="Screenshot 2026-09-14 093827" src="https://github.com/user-attachments/assets/e01d6305-9e6f-4410-9d4c-ae794ee278db" />
example of an autosaved anomaly

<br><br>

<img width="1641" height="846" alt="Screenshot 2026-09-14 091845" src="https://github.com/user-attachments/assets/10487447-9076-4c87-94ad-95e9ab828796" />
detected anomaly 28.69 after manually solved

<br><br>

<img width="1641" height="846" alt="Screenshot 2026-09-14 091845" src="https://github.com/user-attachments/assets/10487447-9076-4c87-94ad-95e9ab828796" />
detected anomaly 28.69 after manually solved

<br><br>

#### Before - within alert tracker
<img width="1460" height="166" alt="image" src="https://github.com/user-attachments/assets/e050f176-698f-49ee-b35f-222e3acc4d65" />
Anomaly 28.69 before being solved

<br><br>

#### After - within resolved alert tracker
<img width="1461" height="158" alt="image" src="https://github.com/user-attachments/assets/b1f1454d-eeb7-4a18-adca-dbe3fb82acb4" />
Anomaly 28.69 after being solved


These are then issued into the alert tracker which then displays it to the user with it's viewable details and editable "alert" and "solved alert" states though a solve button

---

## A search function that filters through the locally display data using keywords and data values

<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/321b3e5c-8b2f-4450-b664-2b5f0e4154d6" />
using "Zone 1" as an example to find all Zone 1 data from a total of 867 data variables!

---


## A sub screen for the API's data viewing
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/b2aeaeed-625e-46d8-b952-738a9426954f" />

This displays the data that is given to the WPF by the API hence why they display the same data:
<img width="1919" height="452" alt="image" src="https://github.com/user-attachments/assets/a5676349-1eab-4f32-80ff-ddc77631cbd1" />

### API only functions
>functions that need the API online or they don't function:


##### Data generator WPF and API
<img width="1919" height="1079" alt="Screenshot 2026-09-14 095015" src="https://github.com/user-attachments/assets/54e9501d-2896-41ae-93de-77f84221a677" />
Generates data for both the API side in batches of 10s and the API + WPF sides in single and batches of 50s
 <br>
API 10s batch gen:
<img width="608" height="50" alt="image" src="https://github.com/user-attachments/assets/f2f4b314-9b2e-4b67-8db2-f4aee2027fe4" />
<br>
API 1s and 50s batch gen:
<img width="208" height="108" alt="image" src="https://github.com/user-attachments/assets/b2827e84-338c-4c15-bb06-3ba4a840f4a1" />


<br><br>

##### Sensor creator
<img width="1919" height="1079" alt="Screenshot 2026-09-14 095600" src="https://github.com/user-attachments/assets/c8ad9536-cb29-4b30-997a-d8602d39dc11" />
Allows the user to create / register new sensor devices and upload files
>These new sensors are only visiblefrom the API view 

<br><br>

<img width="1919" height="1078" alt="image" src="https://github.com/user-attachments/assets/085ad56c-0763-47fb-bbab-97987e89d2ea" />
Sensor 1 - Test Sensor01 No file

Sensor 1 details
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/efeb9a82-72d4-4b10-aed3-d5bca9ac3dbd" />

<br><br>

<img width="1919" height="1079" alt="Screenshot 2026-09-14 101616" src="https://github.com/user-attachments/assets/40982bfd-9823-499b-828a-0da06f1ccb25" />
Sensor 2 - Test Sensor02 file attached

<img width="1919" height="1079" alt="Screenshot 2026-09-14 101630" src="https://github.com/user-attachments/assets/87d6a0ad-10ec-4786-872f-b895f34e6f58" />
Sensor 2 details

---
<br><br>

## Run Performance Tests
These are tests that are done without affecting the overrall data within the WPF or the API through the use of a temp collection that gets wiped the moment the API has finished running the tests.

These tests vary in the number of tests completed at a time:
100, 1 000 and 5 000
<br>
<img width="1919" height="345" alt="image" src="https://github.com/user-attachments/assets/c0e6904d-b6e7-49da-a5b6-68dfe6cd0a79" />
Each test returns it's time taken and will not always deliver the same results due to system interuptions and the occasional system lag
<br><br>

<img width="509" height="382" alt="image" src="https://github.com/user-attachments/assets/7ffabfc0-7138-4f5a-855f-5af90e370ee0" />
<br>
100 fresh insertion performance test

<br><br>

<img width="580" height="424" alt="Screenshot 2026-09-14 102136" src="https://github.com/user-attachments/assets/52b0c13d-11be-4c75-b17f-50dce63c1a82" />
<br>
1 000 fresh insertion performance test

<br><br>

<img width="509" height="382" alt="Screenshot 2026-09-14 102151" src="https://github.com/user-attachments/assets/71bb3c2e-b690-4d1d-93f7-90a50503ff10" />
<br>
5 000 fresh insertion performance test
