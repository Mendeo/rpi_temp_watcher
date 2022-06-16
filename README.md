# Программа для отслеживания температуры процессора Raspberry Pi

При превышении заданной температуры процессора эта программа пошлёт e-mail с предупреждением. Все настройки задаются в конфигурационном файле.  
Температура проверяется каждые 30 секунд.  

Пример конфигурационого файл *config.conf*:

```bash
EMAIL=example@example.com
PASSWORD=<password>
SMTP_SERVER=smtp.example.com
MAX_TEMP = 70
MIN_TEMP = 67
```

* EMAIL: задаётся нужный почтовый адрес;
* PASSWORD: пароль от этой почты;
* SMTP_SERVER: указывается адрес SMTP сервера;
* MAX_TEMP: температура, при которой отсылается e-mail с предупреждением
* MIN_TEMP: температура, при которой снова начинается мониторинг температуры после предыдущего предупреждения.

Для запуска программы в системе должен быть установлен dotnet версии 6.0. Запуск осуществляется следующей командой:

```bash
dotnet watcher.dll
```

Для автоматического запуска программы при старте системы можно создать systemd юнит. Пример файла watcher.service приведён ниже:

```bash
[Unit]
Description=Watching for system parameters and sending email if warnings
After=network.target

[Service]
User=watcher
Type=simple
ExecStart=/usr/local/bin/dotnet /usr/local/lib/rpi_temp_watcher/watcher.dll

[Install]
WantedBy=multi-user.target
```

В данном случае исполняемый код помещён в папку /usr/local/lib/rpi_temp_watcher/