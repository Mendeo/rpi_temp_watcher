/*
    Copyright © 2023 Aleksandr Menyaylo (Александр Меняйло), thesolve@mail.ru, deorathemen@gmail.com

    This file is part of "rpi_temp_watcher".

    "rpi_temp_watcher" is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    "rpi_temp_watcher" is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with "rpi_temp_watcher". If not, see <https://www.gnu.org/licenses/>.
*/
using System.Net.Mail;
using System.Net;
using System.Diagnostics;

const int PAUSE_TIME = 30000;
const string RPI_TEMP_PROCESS = "vcgencmd";
const string RPI_TEMP_COMMAND = "measure_temp";
const int FAN_GPIO_NUMBER = 17;
const string GPIO_SELECT_FILE = "/sys/class/gpio/export";
string FAN_GPIO_DIRECTORY = String.Format("/sys/class/gpio/gpio{0:d}", FAN_GPIO_NUMBER);
string FAN_GPIO_SET_DIRECTION_FILE = Path.Combine(FAN_GPIO_DIRECTORY, "direction");
string FAN_GPIO_VALUE_FILE = Path.Combine(FAN_GPIO_DIRECTORY, "value");

string SETTINGS_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.conf");

/*
FileStreamOptions fso = new FileStreamOptions();
fso.Share = FileShare.ReadWrite;
fso.Access = FileAccess.Read;
fso.Mode = FileMode.Open;
fso.Options = FileOptions.SequentialScan;
StreamReader sr = new StreamReader(fileToRead, fso);
*/
Process _currentProcess = Process.GetCurrentProcess();
bool _isRunnigAsService = _currentProcess.SessionId == _currentProcess.Id;

string[] config;
try
{
	StreamReader sr = new StreamReader(SETTINGS_FILE);
	config = sr.ReadToEnd().Split('\n');
	sr.Close();
}
catch (Exception ex)
{
	Console.WriteLine(ex.Message);
	return;
}
string? EMAIL = null;
string? PASSWORD = null;
string? SMTP_SERVER = null;
double EMAIL_MAX_TEMP = double.NaN;
double EMAIL_MIN_TEMP = double.NaN;
double FAN_ON_TEMP = double.NaN;
double FAN_OFF_TEMP = double.NaN;

bool _alreadySent = false;
bool _fanIsOn = false;

readConfig();
GPIO_init();

Console.WriteLine("EMAIL: " + EMAIL);
Console.WriteLine("Alarm CPU temperature is " + EMAIL_MAX_TEMP.ToString() + "°C");
Console.WriteLine("Cooldown CPU temperature is " + EMAIL_MIN_TEMP.ToString() + "°C");
Console.WriteLine("Fan on temperature is " + FAN_ON_TEMP.ToString() + "°C");
Console.WriteLine("Fan off temperature is " + FAN_OFF_TEMP.ToString() + "°C");


while(true)
{
	//string? rawData = sr.ReadLine();
	//sr.BaseStream.Position = 0;
	string? rawData = executeCommand(RPI_TEMP_PROCESS, RPI_TEMP_COMMAND);
	double? tC = convertTemp(rawData);
	if (tC is null)
	{
		Console.WriteLine("Can't get temperature.");
		Thread.Sleep(PAUSE_TIME);
		continue;
	}
	checkForAlarm((double)tC);
	fanControl((double)tC);
	Thread.Sleep(PAUSE_TIME);
}

void GPIO_init()
{
	StreamWriter sw;
	if (!File.Exists(FAN_GPIO_SET_DIRECTION_FILE))
	{
		try
		{
			sw = new StreamWriter(GPIO_SELECT_FILE);
			sw.WriteLine(FAN_GPIO_NUMBER.ToString());
			sw.Flush();
			sw.Close();
		}
		catch (Exception ex)
		{
			throw new Exception("Can't initialize GPIO: " + ex.Message);
		}
		Thread.Sleep(1000);
		if (File.Exists(FAN_GPIO_SET_DIRECTION_FILE))
		{
			setGPIOdirection();
		}
		else
		{
			throw new Exception("Can't initialize GPIO: Fan GPIO directory does not exist.");
		}
	}
	bool isOut = isOutGPIOdirection();
	if (!isOut)
	{
		setGPIOdirection();
		isOut = isOutGPIOdirection();
		if (!isOut) throw new Exception("Can't initialize GPIO: can't set GPIO out mode.");
	}
	_fanIsOn = readFanStatus();
}

bool readFanStatus()
{
	StreamReader sr = new StreamReader(FAN_GPIO_VALUE_FILE);
	try
	{
		string? data = sr.ReadLine();
		const string msg = "value file must contain \"0\" or \"1\"";
		if (data is null)
		{
			throw new Exception(msg);
		}
		else if (data.Equals("0"))
		{
			return false;
		}
		else if (data.Equals("1"))
		{
			return true;
		}
		else
		{
			throw new Exception(msg);
		}
	}
	catch (Exception ex)
	{
		throw new Exception("Can't initialize GPIO: " + ex.Message);
	}
	finally
	{
		sr.Close();
	}
}

void setGPIOdirection()
{
	try
	{
		StreamWriter sw = new StreamWriter(FAN_GPIO_SET_DIRECTION_FILE);
		sw.WriteLine("out");
		sw.Flush();
		sw.Close();
	}
	catch (Exception ex)
	{
		throw new Exception("Can't initialize GPIO: " + ex.Message);
	}
}

bool isOutGPIOdirection()
{
	StreamReader sr = new StreamReader(FAN_GPIO_SET_DIRECTION_FILE);
	try
	{
		string? data = sr.ReadLine();
		const string msg = "direction file must contain \"in\" or \"out\"";
		if (data is null)
		{
			throw new Exception(msg);
		}
		else if (data.Equals("out"))
		{
			return true;
		}
		else if (data.Equals("in"))
		{
			return false;
		}
		else
		{
			throw new Exception(msg);
		}
	}
	catch (Exception ex)
	{
		throw new Exception("Can't initialize GPIO: " + ex.Message);
	}
	finally
	{
		sr.Close();
	}
}

void fanToggle(bool isOn)
{
	string value = isOn ? "1" : "0";
	try
	{
		StreamWriter fanFile = new StreamWriter(FAN_GPIO_VALUE_FILE);
		fanFile.WriteLine(value);
		fanFile.Flush();
		fanFile.Close();
	}
	catch (Exception ex)
	{
		throw new Exception("Error while setting GPIO value: " + ex.Message);
	}
}
void fanOn()
{
	consoleLog("Fan is on.");
	fanToggle(true);
}

void fanOff()
{
	consoleLog("Fan is off.");
	fanToggle(false);
}

void fanControl(double tC)
{
	if (!_fanIsOn && tC >= FAN_ON_TEMP)
	{
		_fanIsOn = true;
		fanOn();
	}
	else if (_fanIsOn && tC <= FAN_OFF_TEMP)
	{
		_fanIsOn = false;
		fanOff();
	}
}

void checkForAlarm(double tC)
{
	//Console.WriteLine(tC + "     ");
	//Console.SetCursorPosition(0, 0);

	//Checking for alarm
	if (tC >= EMAIL_MAX_TEMP)
	{
		if (!_alreadySent)
		{
			sendEmail(tC);
			_alreadySent = true;
		}
	}
	else if (_alreadySent && tC <= EMAIL_MIN_TEMP)
	{
		_alreadySent = false;
		Console.WriteLine("CPU cooled down.");
	}
}

void readConfig()
{
	try
	{
		foreach (string line in config)
		{
			string[] aux = line.Split('=');
			string test = aux[0].Trim().ToUpper();
			string data = aux[1].Replace("\r", String.Empty).Trim();
			if (test.Equals("EMAIL"))
			{
				EMAIL = data;
			}
			else if (test.Equals("PASSWORD"))
			{
				PASSWORD = data;
			}
			else if (test.Equals("SMTP_SERVER"))
			{
				SMTP_SERVER = data;
			}
			else if (test.Equals("EMAIL_MIN_TEMP"))
			{
				if (!double.TryParse(data, out EMAIL_MIN_TEMP)) throw new Exception("Error in cooldown temperature");
			}
			else if (test.Equals("EMAIL_MAX_TEMP"))
			{
				if (!double.TryParse(data, out EMAIL_MAX_TEMP)) throw new Exception("Error in alarm temperature");
			}
			else if (test.Equals("FAN_ON_TEMP"))
			{
				if (!double.TryParse(data, out FAN_ON_TEMP)) throw new Exception("Error in alarm temperature");
			}
			else if (test.Equals("FAN_OFF_TEMP"))
			{
				if (!double.TryParse(data, out FAN_OFF_TEMP)) throw new Exception("Error in alarm temperature");
			}
			if (EMAIL != null && PASSWORD != null && SMTP_SERVER != null && !double.IsNaN(EMAIL_MIN_TEMP) && !double.IsNaN(EMAIL_MAX_TEMP) && !double.IsNaN(FAN_ON_TEMP) && !double.IsNaN(FAN_OFF_TEMP)) break;
		}
		if (EMAIL_MAX_TEMP <= EMAIL_MIN_TEMP) throw new Exception("EMAIL_MAX_TEMP must be greater then EMAIL_MIN_TEMP!");
		if (FAN_ON_TEMP <= FAN_OFF_TEMP) throw new Exception("FAN_ON_TEMP must be greater then FAN_OFF_TEMP!");
		if (EMAIL == null || PASSWORD == null || SMTP_SERVER == null || double.IsNaN(EMAIL_MIN_TEMP) || double.IsNaN(EMAIL_MAX_TEMP) || double.IsNaN(FAN_ON_TEMP) || double.IsNaN(FAN_OFF_TEMP)) throw new Exception("Error in configuration file");
	}
	catch (Exception e)
	{
		Console.WriteLine(e.Message);
		Environment.Exit(1);
	}
}
void sendEmail(double? tC)
{
	if (tC is null) return;
	const string SUBJECT = "Overheat warning!";
	string BODY = "CPU temperature exceeded " + EMAIL_MAX_TEMP.ToString() + "° and now equal " + tC.ToString() + "°!";
	const bool NEED_SSL = true;

	SmtpClient client = new SmtpClient(SMTP_SERVER);
	client.EnableSsl = NEED_SSL;
	client.Credentials = new NetworkCredential(EMAIL, PASSWORD);
	if (EMAIL is null) throw new Exception("Email address can't be null");
	MailMessage msg = new MailMessage(EMAIL, EMAIL, SUBJECT, BODY);
	msg.IsBodyHtml = false;
	try
	{
		client.Send(msg);
		string logMsg = "Message sent successfully!";
		consoleLog(logMsg);
	}
	catch(Exception ex)
	{
		consoleLog(ex.Message);
	}
	finally
	{
		client.Dispose();
	}
}

void consoleLog(string msg)
{
	if (_isRunnigAsService)
	{
		Console.WriteLine(msg);
	}
	else
	{
		DateTime dateNow = DateTime.Now;
		Console.WriteLine(dateNow.ToShortDateString() + ", " + dateNow.ToLongTimeString() + ": " + msg);
	}
}

string? executeCommand(string procName, string argsString)
{
	try
	{
		ProcessStartInfo procStartInfo = new ProcessStartInfo(procName);
		// The following commands are needed to redirect the standard output.
		// This means that it will be redirected to the Process.StandardOutput StreamReader.
		procStartInfo.RedirectStandardOutput = true;
		procStartInfo.UseShellExecute = false;
		// Do not create the black window.
		procStartInfo.CreateNoWindow = true;
		procStartInfo.Arguments = argsString;
		// Now we create a process, assign its ProcessStartInfo and start it
		Process proc = new Process();
		proc.StartInfo = procStartInfo;
		proc.Start();
		// Get the output into a string
		return proc.StandardOutput.ReadToEnd();
	}
	catch (Exception ex)
	{
		Console.WriteLine(ex.Message);
		Environment.Exit(2);
		return null;
	}
}

double? convertTemp(string? rawData)
{
	if (rawData is null) return null;
	string aux = rawData.Split('=')[1];
	int index = aux.IndexOf('\'');
	if (index == -1) return null;
	string value = aux.Substring(0, index);
	double tC;
	if (!double.TryParse(value, out tC)) return null;
	return tC;
}