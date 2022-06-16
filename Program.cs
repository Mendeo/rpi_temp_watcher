/*
    Copyright © 2022 Aleksandr Menyaylo (Александр Меняйло), thesolve@mail.ru, deorathemen@gmail.com

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

const int PAUSE_TIME = 30000;
const string RPI_TEMP_PROCESS = "vcgencmd";
const string RPI_TEMP_COMMAND = "measure_temp";
string EMAIL_SETTINGS_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.conf");

/*
FileStreamOptions fso = new FileStreamOptions();
fso.Share = FileShare.ReadWrite;
fso.Access = FileAccess.Read;
fso.Mode = FileMode.Open;
fso.Options = FileOptions.SequentialScan;
StreamReader sr = new StreamReader(fileToRead, fso);
*/

string[] emailData;
try
{
	StreamReader sr = new StreamReader(EMAIL_SETTINGS_FILE);
	emailData = sr.ReadToEnd().Split('\n');
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
double MAX_TEMP = double.NaN;
double MIN_TEMP = double.NaN;

try
{
	foreach (string line in emailData)
	{
		string[] aux = line.Split('=');
		string test = aux[0].Trim().ToUpper();
		string data = aux[1].Replace("\r", String.Empty).Trim();
		if (test.Equals("EMAIL"))
		{
			EMAIL = data;
		}
		else if(test.Equals("PASSWORD"))
		{
			PASSWORD = data;
		}
		else if(test.Equals("SMTP_SERVER"))
		{
			SMTP_SERVER = data;
		}
		else if (test.Equals("MIN_TEMP"))
		{
			if (!double.TryParse(data, out MIN_TEMP)) throw new Exception("Error in cooldown temperature");
		}
		else if (test.Equals("MAX_TEMP"))
		{
			if (!double.TryParse(data, out MAX_TEMP)) throw new Exception("Error in alarm temperature");
		}
		if (EMAIL != null && PASSWORD != null && SMTP_SERVER != null && !double.IsNaN(MIN_TEMP) && !double.IsNaN(MAX_TEMP)) break;
	}
	if (MAX_TEMP <= MIN_TEMP) throw new Exception("MAX_TEMP must be greater then MIN_TEMP!");
	if (EMAIL == null || PASSWORD == null || SMTP_SERVER == null || double.IsNaN(MIN_TEMP) || double.IsNaN(MAX_TEMP)) throw new Exception("Error in configuration file");
}
catch (Exception e)
{
	Console.WriteLine(e.Message);
	Environment.Exit(1);
}

Console.WriteLine("EMAIL: " + EMAIL);
Console.WriteLine("Alarm CPU temperature is " + MAX_TEMP.ToString() + "°C");
Console.WriteLine("Cooldown CPU temperature is " + MIN_TEMP.ToString() + "°C");
bool alreadySent = false;
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
	//Console.WriteLine(tC + "     ");
	//Console.SetCursorPosition(0, 0);
	if (tC >= MAX_TEMP)
	{
		if (!alreadySent)
		{
			sendEmail(tC);
			alreadySent = true;
		}
	}
	else if (alreadySent && tC <= MIN_TEMP)
	{
		alreadySent = false;
	}
	Thread.Sleep(PAUSE_TIME);
}

void sendEmail(double? tC)
{
	if (tC is null) return;
	const string SUBJECT = "Overheat warning!";
	string BODY = "CPU temperature exceeded " + MAX_TEMP.ToString() + "° and now equal " + tC.ToString() + "°!";
	const bool NEED_SSL = true;

	SmtpClient client = new SmtpClient(SMTP_SERVER);
	client.EnableSsl = NEED_SSL;
	client.Credentials = new NetworkCredential(EMAIL, PASSWORD);
	MailMessage msg = new MailMessage(EMAIL, EMAIL, SUBJECT, BODY);
	msg.IsBodyHtml = false;
	try
	{
		client.Send(msg);
		Console.WriteLine(DateTime.Now.ToLongDateString() + "Message sent successfully!");
	}
	catch(Exception ex)
	{
		Console.WriteLine(DateTime.Now.ToLongDateString() + " " + ex.Message);
	}
	finally
	{
		client.Dispose();
	}
}

string? executeCommand(string procName, string argsString)
{
	try
	{
		System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo(procName);
		// The following commands are needed to redirect the standard output.
		// This means that it will be redirected to the Process.StandardOutput StreamReader.
		procStartInfo.RedirectStandardOutput = true;
		procStartInfo.UseShellExecute = false;
		// Do not create the black window.
		procStartInfo.CreateNoWindow = true;
		procStartInfo.Arguments = argsString;
		// Now we create a process, assign its ProcessStartInfo and start it
		System.Diagnostics.Process proc = new System.Diagnostics.Process();
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