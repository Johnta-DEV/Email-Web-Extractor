﻿using Microsoft.Win32;
using System;
using System.Management;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Diagnostics;

namespace Email_Web_Extractor
{
    public partial class MainForm : Form
    {
        public const int MAX_GOOGLE_REQUEST_THREADS = 10;
        public const int MAX_GOOGLE_REQUEST_PAGES_COUNT = 10;
        public const int THREADS_MULTIPLICATION_RATIO = 5;

        private static string rootDirectory = "";
        private static string tempFileName = "Временный файл сайтов.txt";
        private static string fullTempFilePath;

        private static string emailsFolderName = "Emails";
        private static string emailsDirectory;
        private static string webAddresesListFileName = "Сайты.txt";
        private static string webAddresesListFilePath;
        private static string pagesListFileName = "Страницы.txt";
        private static string pagesListFilePath;
        private static string emailListFileName = "Имейлы.txt";
        private static string emailListFilePath;

        private InfoForm infoForm;
        private InstructionForm instructionForm;

        // $request = "https://www.googleapis.com/customsearch/v1?key=$key&start=$offset&cx=$cx&q=$url";

        Color normalColor = Color.FromKnownColor(KnownColor.Highlight);
        Color greenColor = Color.FromKnownColor(KnownColor.ForestGreen);
        Color yellowColor = Color.FromArgb(255, 200, 150, 0);
        Color redColor = Color.FromKnownColor(KnownColor.OrangeRed);

        Color disabledTextColor = Color.FromKnownColor(KnownColor.ActiveBorder);
        Color disabledBGColor = Color.FromKnownColor(KnownColor.Control);

        bool isAPIHandlerInit = false;

        Stopwatch googleRequestStopWatch;
        Stopwatch emailsSearchStopWatch;

        List<SiteEmailRecord[]> siteRecordsList = new List<SiteEmailRecord[]>();
        List<SiteEmailRecord> foundEmailsList = new List<SiteEmailRecord>();

        int currentSearchPages = 10;

        int currentMaxAvalableThreads = 1;
        Queue<int> googleSearchOffsetsQueue = new Queue<int>();
        List<Task> activeGoogleSearchThreads = new List<Task>();

        Queue<string> sitesToCheckEmailsQueue = new Queue<string>();
        List<Task> activeEmailsSearchThreads = new List<Task>();

        bool canDoGoogleRequest = true;
        bool candDoEmailsSearch = true;

        bool isGoogleSearching = false;
        bool isEmailsSearching = false;

        bool isAnyTextEnteredInRequest = false;

        bool isSavingEmails = false;

        int totalPagesToSearchEmailsCount = 0;
        int currentPagesToSearchEmailsCount = 0;

        string additionalMessage = "";

        public event TaskComplited GoogleRequestTaskComplitedEvent;
        public event EmailSearch GoogleSearchStartedEvent;
        public event EmailSearch GoogleSearchEndedEvent;
        public event TaskComplited EmasilSearchTaskComplitedEvent;
        public event EmailSearch EmailSearchStartedEvent;
        public event EmailSearch EmailSearchEndedEvent;





        /// <summary>
        /// Конструктор
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            rootDirectory = AppDomain.CurrentDomain.BaseDirectory;
            fullTempFilePath = rootDirectory + tempFileName;
            emailsDirectory = rootDirectory + emailsFolderName;

            Directory.CreateDirectory(emailsDirectory);

            webAddresesListFilePath = emailsDirectory + "\\" + webAddresesListFileName;
            pagesListFilePath = emailsDirectory + "\\" + pagesListFileName;
            emailListFilePath = emailsDirectory + "\\" + emailListFileName;

            webPagesTempFilePathTextBox.Text = fullTempFilePath;
            sitesPathFileLabel.Text = webAddresesListFilePath;
            emailsPathFileLabel.Text = emailListFilePath;

            // Получение кол-ва макс потоков
            int cpuCores = GetCPUThreadsCount();
            int totalThreads = cpuCores * THREADS_MULTIPLICATION_RATIO;
            for (int i = 0; i < totalThreads; i++)
            {
                coresCountComboBox.Items.Add(i + 1);
            }
            coresCountComboBox.SelectedIndex = Clamp(0, totalThreads - 1, totalThreads - 1);

            // Кол-во запрашиваемых страниц Google
            for (int i = 1; i <= MAX_GOOGLE_REQUEST_PAGES_COUNT; i++)
            {
                pagesCountComboBox.Items.Add(i);
            }
            pagesCountComboBox.SelectedIndex = MAX_GOOGLE_REQUEST_PAGES_COUNT - 1;

            GoogleRequestTaskComplitedEvent += OnGoogleRequestTaskComplited;
            GoogleSearchStartedEvent += OnGoogleSearchStarted;
            GoogleSearchEndedEvent += OnGoogleSearchEnded;
            EmasilSearchTaskComplitedEvent += OnEmailTaskComplited;
            EmailSearchStartedEvent += OnEmailSearchStarted;
            EmailSearchEndedEvent += OnEmailSearchEnded;

            // Отключить кнопку поиска на старте
            findWebSitesButton.Enabled = false;
            findWebSitesButton.BackColor = disabledBGColor;
            findWebSitesButton.ForeColor = disabledTextColor;


            this.isAPIHandlerInit = ApiKeysHandler.Init(rootDirectory, this);

            if (File.Exists(fullTempFilePath))
            {
                candDoEmailsSearch = true;
            }

            this.infoForm = new InfoForm();
            this.instructionForm = new InstructionForm();

        }


        /// <summary>
        /// Получить макс кол-во потоков CPU
        /// </summary>
        /// <returns></returns>
        private int GetCPUThreadsCount()
        {
            return Environment.ProcessorCount;
        }



        /// <summary>
        /// Получить массив имейлов (string) из строки
        /// </summary>
        /// <param name="text">Строка в которой происходит поиск</param>
        /// <returns></returns>
        private string[] GetEmailsFromText(string text)
        {
            //preg_match_all("/[\._a-zA-Z0-9-]+@[\._a-zA-Z0-9-]+\.[\._a-zA-Z0-9-]+/", $text, $result);
            //preg_match_all("[a-zA-Z0-9_.+-]+@[a-zA-Z-]+\.+[a-zA-Z.-]+", $text, $result);
            string regexStr = @"[\._a-zA-Z0-9-]+@[\._a-zA-Z0-9-]+\.[\._a-zA-Z0-9-]+";
            return GetRegexFromString(regexStr, text);
        }

        /// <summary>
        /// Получить подстроку из строки основываясь на RegEx
        /// </summary>
        /// <param name="regexStr">RegEx выражение</param>
        /// <param name="text">Строка в которой происходит поиск</param>
        /// <returns></returns>
        private string[] GetRegexFromString(string regexStr, string text)
        {
            Regex regex = new Regex(regexStr);
            MatchCollection matches = regex.Matches(text);
            string[] result = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                result[i] = matches[i].Value;
            }
            return result;
        }

        /// <summary>
        /// Запрос в Google (JSON)
        /// </summary>
        /// <param name="searchRequest">Поисковой запрос</param>
        /// <param name="offset">Отступ 0-99</param>
        /// <returns></returns>
        private string RequestGoogleAPI(string searchRequest, int offset)
        {
            ApiKeyPair keyPair = ApiKeysHandler.GetNextApiKey();
            string currentAPIKey = keyPair.key;
            string currentCXKey = keyPair.cx;

            string fullRequest = "https://www.googleapis.com/customsearch/v1?key=" + currentAPIKey + "&" +
                "start=" + offset + "&cx=" + currentCXKey + "&q=" + WebUtility.UrlEncode(searchRequest);


            StringBuilder sb = new StringBuilder();
            WebClient myClient = new WebClient();
            try
            {
                Stream stream = myClient.OpenRead(fullRequest);
                using (Stream response = stream)
                {
                    using (StreamReader sr = new StreamReader(response))
                    {
                        while (sr.EndOfStream == false)
                        {
                            sb.Append(sr.ReadLine());
                        }
                    }
                }
                return sb.ToString();
            }
            catch (WebException e)
            {
                if (e.Message.Contains("(429) Too Many Requests"))
                {
                    Print("Слишком много запросов! Нужно заменить API Key!");
                    additionalMessage = "Слишком много запросов! Нужно заменить API Key!";
                }
                else
                {
                    additionalMessage = e.Message;
                    Print("Ошибка при выполнении HTTP запроса Google: " + e.Message);
                }
                return "";
            }
        }



        private int Clamp(int min, int max, int value)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }


        private void findWebSitesButton_Click(object sender, EventArgs e)
        {
            if (searchRequestTextBox.Text != "")
            {
                StartGoogleRequest(searchRequestTextBox.Text);
            }
            else
            {
                PrintStatusBar("Введите запрос для поиска!", yellowColor);
            }
        }


        private void StartGoogleRequest(string googleRequest)
        {
            try
            {
                if (canDoGoogleRequest)
                {
                    PrintStatusBar("Начался запрос в Google...");
                    additionalMessage = "";
                    GoogleSearchStartedEvent?.Invoke(this, null);

                    for (int i = 0; i < currentMaxAvalableThreads; i++)
                    {
                        if (i >= MAX_GOOGLE_REQUEST_PAGES_COUNT || i >= currentSearchPages)
                            break;
                        googleSearchOffsetsQueue.Enqueue(i * MAX_GOOGLE_REQUEST_PAGES_COUNT);
                    }

                    for (int i = 0; i < currentMaxAvalableThreads; i++)
                    {
                        Task t = null;
                        t = new Task(() =>
                        {
                            while (googleSearchOffsetsQueue.Count > 0)
                            {
                                int currOffset = 0;
                                lock (googleSearchOffsetsQueue)
                                {
                                    if (googleSearchOffsetsQueue.Count > 0)
                                    {
                                        currOffset = googleSearchOffsetsQueue.Dequeue();
                                    }
                                    else break;
                                }
                                GetSitesByRequest(googleRequest, currOffset);
                            }
                            lock (activeGoogleSearchThreads)
                            {
                                activeGoogleSearchThreads.Remove(t);
                            }
                            GoogleRequestTaskComplitedEvent.Invoke(t);
                        });
                        activeGoogleSearchThreads.Add(t);

                        t.Start();

                        if (i >= MAX_GOOGLE_REQUEST_THREADS)
                            break;
                    }
                }
                else
                {
                    PrintStatusBar("Запрос в Google уже выполняется...", yellowColor);
                }

            }
            catch (Exception s)
            {
                PrintStatusBar("Ошибка Google запроса: " + s.Message);
                GoogleSearchEndedEvent?.Invoke(this, null);
            }
        }

        /// <summary>
        /// Записать все записи сайтов во временный файл
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        private void WriteAllRequestResultsToTempFile(string filePath)
        {
            if (siteRecordsList.Count == 0)
            {
                return;
            }

            try
            {
                if (appendToTempFileCheckBox.Checked)
                {
                    if (File.Exists(filePath))
                    {
                        string fileContent = File.ReadAllText(filePath);
                        using (StreamWriter streamWriter = File.AppendText(filePath))
                        {
                            for (int i = 0; i < siteRecordsList.Count; i++)
                            {
                                for (int y = 0; y < siteRecordsList[i].Length; y++)
                                {
                                    string record = (siteRecordsList[i][y].Site).Replace("\n", "");
                                    if (fileContent.Contains(record) == false)
                                    {
                                        streamWriter.WriteLine(record);
                                    }
                                    //else Print("--skipped double...");
                                }
                            }
                        }

                    }
                    else
                    {
                        StringBuilder sb = new StringBuilder();
                        string temp = "";
                        for (int i = 0; i < siteRecordsList.Count; i++)
                        {
                            for (int y = 0; y < siteRecordsList[i].Length; y++)
                            {
                                temp = (siteRecordsList[i][y].Site).Replace("\n", "");
                                sb.Append(temp + "\n");
                            }
                        }
                        File.WriteAllText(filePath, sb.ToString());
                    }
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    string temp = "";
                    for (int i = 0; i < siteRecordsList.Count; i++)
                    {
                        for (int y = 0; y < siteRecordsList[i].Length; y++)
                        {
                            temp = (siteRecordsList[i][y].Site).Replace("\n", "");
                            sb.Append(temp + "\n");
                        }
                    }
                    File.WriteAllText(filePath, sb.ToString());
                }
            }
            catch (Exception e)
            {
                PrintStatusBar("Ошибка при обработке временного файла: " + e.Message);
            }
        }

        private static bool ContainsFewDotsInRow(string text)
        {
            if (text == null)
                return false;
            bool isDot = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '.')
                {
                    if (isDot)
                    {
                        return true;
                    }
                    else
                    {
                        isDot = true;
                    }
                }
                else
                {
                    isDot = false;
                }
            }
            return false;
        }

        /// <summary>
        /// Запросить страницу результатов Google запроса. (макс 10 результатов)
        /// </summary>
        /// <param name="googleRequest">Строка запроса</param>
        /// <param name="offset">Отступ от начала (в результатах, а не в страницах)</param>
        private void GetSitesByRequest(string googleRequest, int offset = 0)
        {
            //try
            //{
            //googleRequestStopWatch = new Stopwatch();
            string responce = RequestGoogleAPI(googleRequest, offset);

            // Запросов не найдено... возможна ошибка или привышение лимита запросов.
            if (responce == "")
                return;

            JObject jo = JObject.Parse(responce);

            JToken token = jo["items"];

            // Если результатов нет...
            if (token == null)
                return;
            //Print(jo.ToString());
            int count = token.Count();
            SiteEmailRecord[] records = new SiteEmailRecord[count];
            for (int i = 0; i < count; i++)
            {
                string str = token[i].ToString();
                JObject jo1 = JObject.Parse(str);
                string page = jo1["link"].ToString();
                string site = jo1["displayLink"].ToString();
                SiteEmailRecord record = new SiteEmailRecord();
                record.Site = site;
                record.Page = page;
                records[i] = record;
            }
            siteRecordsList.Add(records);
            //}
            //catch (WebException e)
            //{
            //    PrintStatusBar("Ошибка HTTP: " + e.Message, redColor);
            //}
            //catch (Exception e)
            //{
            //    PrintStatusBar("Ошибка при выполнении Google запроса: : " + e.Message, redColor);
            //}

            //Console.WriteLine(jo1["link"].ToString());
            //Console.WriteLine("Token " + i +": " + str);
            //File.WriteAllText(fullTempFilePath, responce);
            //JArray ja = (JArray)JsonConvert.DeserializeObject(responce);
            //JObject jo = (JObject)ja[0];

            //JToken token = jo["items"];
            //JTokenReader reader = new JTokenReader(token);
            //reader.Read();
        }

        /// <summary>
        /// Поиск имейлов на сайте
        /// </summary>
        /// <param name="site">URL адрес сайта</param>
        private void FindEmailsOnSite(string site)
        {
            StringBuilder sb = new StringBuilder();

            site = "http://" + site;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(site);
                request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:47.0) Gecko/20100101 Firefox/47.0";
                //request.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:47.0) Gecko/20100101 Firefox/47.0");
                request.Timeout = 2500;
                using (WebResponse webResponse = request.GetResponse())
                {
                    using (Stream response = webResponse.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(response))
                        {
                            while (sr.EndOfStream == false)
                            {
                                sb.Append(sr.ReadLine());
                            }
                        }
                    }
                }


                Uri siteUri = new Uri(site);
                string siteName = siteUri.Host;
                string page = site;
                string[] emails = GetEmailsFromText(sb.ToString());
                SiteEmailRecord record = new SiteEmailRecord(siteName, page, emails);
                foundEmailsList.Add(record);
            }
            catch (Exception e)
            {
                Print("При поиске имейлов возникла ошибка: " + e.Message);
            }
        }

        [Obsolete]
        private string GetTokenKeys(JToken jo)
        {
            //JEnumerable<JToken> childrens = jo.Children();
            //string[] result = new string[childrens.Count()];
            //
            //for (int i = 0; i < result.Length; i++)
            //{
            //    JTokenReader reader = new JTokenReader(childrens.ElementAt(i));
            //    reader.Read();
            //    result[i] = (string)reader.Value;
            //    Console.WriteLine("JToken: " + reader.Value);
            //}
            //return result;
            JTokenReader reader = new JTokenReader(jo);
            reader.Read();
            return (string)reader.Value;
        }




        private void PrintStatusBar(string text)
        {
            PrintStatusBar(text, normalColor);
        }
        private void PrintStatusBar(string text, Color color)
        {
            this.Invoke((Action)(() => statusBarText.Text = text));
            this.Invoke((Action)(() => statusBar.BackColor = color));
            Print(text);
        }
        private void Print(string text)
        {
            Console.WriteLine(text);
        }
        private void Print(int text)
        {
            Console.WriteLine(text);
        }




        private void webPagesTempFilePathTextBox_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = rootDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void deleteWebPagesTempFileButton_Click(object sender, EventArgs e)
        {
            bool isExists = false;
            bool existsAfterDelete = false;
            if (File.Exists(fullTempFilePath))
            {
                isExists = true;
            }
            File.Delete(fullTempFilePath);
            if (File.Exists(fullTempFilePath))
            {
                existsAfterDelete = true;
            }

            if (isExists)
            {
                if (existsAfterDelete)
                {
                    PrintStatusBar("Не удалось удалить временный файл...", redColor);
                }
                else
                {
                    PrintStatusBar("Временный файл успешно удалён!", greenColor);
                }
            }
            else
            {
                PrintStatusBar("Временный итак не существует!", greenColor);
            }

            UpdateButtons();
        }

        private void openWebPagesTempFileButton_Click(object sender, EventArgs e)
        {
            if (File.Exists(fullTempFilePath))
            {
                Process openFileProcess = new Process();
                openFileProcess.StartInfo.FileName = fullTempFilePath;
                openFileProcess.Start();
            }
            else
            {
                UpdateButtons();
                PrintStatusBar("Невозможно открыть временный файл. Его нет!", yellowColor);
            }
        }

        private void coresCountComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentMaxAvalableThreads = coresCountComboBox.SelectedIndex + 1;
            //Print(currentAvalableThrads);
        }
        private void pagesCountComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentSearchPages = Clamp(1, MAX_GOOGLE_REQUEST_PAGES_COUNT, pagesCountComboBox.SelectedIndex + 1);
        }
        private void searchRequestTextBox_TextChanged(object sender, EventArgs e)
        {
            if (searchRequestTextBox.Text != "")
            {
                isAnyTextEnteredInRequest = true;
            }
            else
            {
                isAnyTextEnteredInRequest = false;
            }
            UpdateButtons();
        }
        private void sitesPathFileLabel_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = emailsDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void emailsPathFileLabel_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = emailsDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        private void findEmailsButton_Click(object sender, EventArgs e)
        {
            if (candDoEmailsSearch)
            {
                PrintStatusBar("Поиск имейлов начался...", normalColor);
                this.currentPagesToSearchEmailsCount = 0;

                EmailSearchStartedEvent?.Invoke(this, null);

                string[] pagesToSearchArr = null;
                if (pagesNamesTextBox.Text != "")
                {
                    try
                    {
                        string[] temp = (pagesNamesTextBox.Text).Split(' ');
                        List<string> addresesList = new List<string>();

                        addresesList.Add("");
                        for (int i = 0; i < temp.Length; i++)
                        {
                            string t = temp[i];
                            if (t != "")
                            {
                                addresesList.Add(t);
                            }
                        }
                        pagesToSearchArr = addresesList.ToArray();
                    }
                    catch
                    {
                        pagesNamesTextBox.Text = "";
                        PrintStatusBar("Страницы поиска заданы некорректно!", yellowColor);
                        return;
                    }
                }

                // Создание списка страниц
                string tempFileContent;
                string[] sitesArr;
                if (File.Exists(fullTempFilePath) == false)
                {
                    PrintStatusBar("Временный файл со списком сайтов не существует!", redColor);
                    return;
                }
                try
                {
                    tempFileContent = File.ReadAllText(fullTempFilePath);
                    sitesArr = tempFileContent.Split('\n');

                    if (sitesArr == null)
                    {
                        PrintStatusBar("Временный файл не содержит web-адресов!", yellowColor);
                        return;
                    }
                    if (sitesArr.Length <= 1)
                    {
                        PrintStatusBar("Временный файл не содержит web-адресов!", yellowColor);
                        return;
                    }
                }
                catch
                {
                    PrintStatusBar("Не удалось открыть временный файл!", redColor);

                    return;
                }

                List<string> tempAddreses = new List<string>();
                int sitesCount = 0;

                for (int i = 0; i < sitesArr.Length; i++)
                {
                    string site = sitesArr[i];
                    if (site != "")
                    {
                        sitesCount++;
                        for (int y = 0; y < pagesToSearchArr.Length; y++)
                        {
                            tempAddreses.Add(site + "/" + pagesToSearchArr[y]);
                        }
                    }
                }

                this.totalPagesToSearchEmailsCount = tempAddreses.Count;
                //int currentPagesToSearchEmailsCount = 0;

                Print("\nВсего сайтов: " + sitesCount + "; Страниц на каждый сайт: " + pagesToSearchArr.Length +
                    "; Всего страниц для проверки: " + tempAddreses.Count);

                for (int i = 0; i < tempAddreses.Count; i++)
                {
                    sitesToCheckEmailsQueue.Enqueue(tempAddreses[i]);
                }

                // Запуск асинхронных потоков поиска
                for (int i = 0; i < currentMaxAvalableThreads; i++)
                {
                    Task t = null;
                    t = new Task(() =>
                    {
                        while (sitesToCheckEmailsQueue.Count > 0)
                        {
                            string currentSite = "";
                            lock (sitesToCheckEmailsQueue)
                            {
                                if (sitesToCheckEmailsQueue.Count > 0)
                                {
                                    currentSite = sitesToCheckEmailsQueue.Dequeue();
                                }
                                else break;
                            }
                            Print("Поток " + t.Id + " взял задачу. Осталось: " + sitesToCheckEmailsQueue.Count + ". Всего потоков: " + currentMaxAvalableThreads);
                            FindEmailsOnSite(currentSite);
                            currentPagesToSearchEmailsCount++;

                            // Вывод в статус бар оповещения о кол-ве имейлов
                            this.Invoke((Action)(() =>
                            {
                                PrintStatusBar("Надено имейлов: " + foundEmailsList.Count + " Обработано сайтов: " + currentPagesToSearchEmailsCount + "/" + totalPagesToSearchEmailsCount);
                            }
                            ));
                        }
                        lock (activeEmailsSearchThreads)
                        {
                            activeEmailsSearchThreads.Remove(t);
                            Print("Потол " + t.Id + " завершился! Осталось потоков: " + activeEmailsSearchThreads.Count);
                        }
                        EmasilSearchTaskComplitedEvent?.Invoke(t);
                    });
                    activeEmailsSearchThreads.Add(t);
                    t.Start();
                }
            }
            else PrintStatusBar("Невозможно выполнить поиск имейлов!", yellowColor);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (isAPIHandlerInit == false)
            {
                PrintStatusBar("Файл с API ключами не содержит ни одного ключа или отсутствует!", redColor);
            }
            UpdateButtons();
        }
        private void выходToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void оПрограммеToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.infoForm.ShowDialog();
        }
        private void иструкцияToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.instructionForm.Show();
        }

















        private void OnGoogleRequestTaskComplited(Task task)
        {
            //Print("\\\\Threads: " + currentActiveThreads);
            lock (activeGoogleSearchThreads)
            {
                if (activeGoogleSearchThreads.Count == 0)
                {
                    GoogleSearchEndedEvent?.Invoke(this, null);
                    if (additionalMessage == "")
                    {
                        PrintStatusBar("Запрос выполнен! " + ((float)(googleRequestStopWatch.ElapsedMilliseconds) * 0.001f) + "с", greenColor);
                    }
                    else
                    {
                        PrintStatusBar("При выполнении запроса возникли проблемы: " + additionalMessage + ". " + ((float)(googleRequestStopWatch.ElapsedMilliseconds) * 0.001f) + "с", yellowColor);
                    }
                }
                //else Print("--Поток завершился; " + activeThreads.Count);
            }
        }
        private void OnEmailTaskComplited(Task task)
        {
            lock (activeEmailsSearchThreads)
            {
                if (activeEmailsSearchThreads.Count == 0 && isSavingEmails == false)
                {
                    isSavingEmails = true;
                    //int emailsCount = 0;
                    //for (int i = 0; i < foundEmailsList.Count; i++)
                    //{
                    //    SiteEmailRecord record = foundEmailsList[i];
                    //    if (record.Emails != null)
                    //    {
                    //        emailsCount += record.Emails.Length;
                    //    }
                    //}

                    //foundRelativeEmailsCount
                    // Фильтрация и запись имейлов в соответствующие файлы
                    List<string> uniqueEmails = new List<string>();
                    List<string> sites = new List<string>();
                    List<string> pages = new List<string>();
                    for (int i = 0; i < foundEmailsList.Count; i++)
                    {
                        SiteEmailRecord record = foundEmailsList[i];
                        if (record.Emails != null)
                        {
                            for (int d = 0; d < record.Emails.Length; d++)
                            {
                                string currentEmail = record.Emails[d];
                                if (currentEmail.EndsWith(".png") == false && currentEmail.EndsWith(".webp") == false
                                    && currentEmail.EndsWith(".jpg") == false && ContainsFewDotsInRow(currentEmail) == false)
                                {
                                    currentEmail = currentEmail.TrimEnd('.');
                                    if (currentEmail != "")
                                    {
                                        if (uniqueEmails.Contains(currentEmail) == false)
                                        {
                                            uniqueEmails.Add(currentEmail);
                                            sites.Add(record.Site);
                                            pages.Add(record.Page);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    try
                    {
                        File.Delete(webAddresesListFilePath);
                        File.Delete(pagesListFilePath);
                        File.Delete(emailListFilePath);
                        StreamWriter sitesWriter = File.AppendText(webAddresesListFilePath);
                        StreamWriter pagesWriter = File.AppendText(pagesListFilePath);
                        StreamWriter emailsWriter = File.AppendText(emailListFilePath);
                        for (int i = 0; i < uniqueEmails.Count; i++)
                        {
                            sitesWriter.WriteLine(sites[i]);
                            pagesWriter.WriteLine(pages[i]);
                            emailsWriter.WriteLine(uniqueEmails[i]);
                        }
                        sitesWriter.Close();
                        pagesWriter.Close();
                        emailsWriter.Close();

                        EmailSearchEndedEvent?.Invoke(this, null);
                        PrintStatusBar("Поиск имейлов завершен. " + ((float)(emailsSearchStopWatch.ElapsedMilliseconds) * 0.001f) + "с" + " Найдено уникальных имейлов: " + uniqueEmails.Count + " Обработанно страниц: " + currentPagesToSearchEmailsCount + "/" + totalPagesToSearchEmailsCount, greenColor);

                    }
                    catch (Exception e)
                    {
                        EmailSearchEndedEvent?.Invoke(this, null);
                        PrintStatusBar("Не удалось записать найденные имейлы в файлы! " + ((float)(emailsSearchStopWatch.ElapsedMilliseconds) * 0.001f) + "с" + " Найдено имейлов: " + uniqueEmails.Count + " Ошибка: " + e.Message, redColor);

                    }

                    //if (additionalMessage == "")
                    //{
                    //    PrintStatusBar("Запрос выполнен! " + ((float)(googleRequestStopWatch.ElapsedMilliseconds) * 0.001f) + "с", greenColor);
                    //}
                    //else
                    //{
                    //    PrintStatusBar("При выполнении запроса возникли проблемы: " + additionalMessage + ". " + ((float)(googleRequestStopWatch.ElapsedMilliseconds) * 0.001f) + "с", yellowColor);
                    //}
                }
            }
        }

        // Запрос начался
        private void OnGoogleSearchStarted(object sender, EventArgs e)
        {
            canDoGoogleRequest = false;
            isGoogleSearching = true;

            UpdateButtons();

            siteRecordsList.Clear();

            googleRequestStopWatch = new Stopwatch();
            googleRequestStopWatch.Start();
        }
        // Запрос завершился
        private void OnGoogleSearchEnded(object sender, EventArgs e)
        {
            WriteAllRequestResultsToTempFile(fullTempFilePath);
            googleRequestStopWatch.Stop();

            isGoogleSearching = false;
            canDoGoogleRequest = true;

            UpdateButtons();
        }
        private void OnEmailSearchStarted(object sender, EventArgs e)
        {
            candDoEmailsSearch = false;
            isSavingEmails = false;
            isEmailsSearching = true;

            UpdateButtons();

            //foundRelativeEmailsCount = 0;
            foundEmailsList.Clear();

            emailsSearchStopWatch = new Stopwatch();
            emailsSearchStopWatch.Start();

        }

        private void OnEmailSearchEnded(object sender, EventArgs e)
        {
            emailsSearchStopWatch.Stop();


            candDoEmailsSearch = true;
            isEmailsSearching = false;

            UpdateButtons();
        }






        private void UpdateButtons()
        {
            bool isAnySearchInProgress = false;
            if (isGoogleSearching || isEmailsSearching)
                isAnySearchInProgress = true;

            bool isTempFileExists = File.Exists(fullTempFilePath);

            if (isAnySearchInProgress || isAPIHandlerInit == false)
            {
                this.Invoke((Action)(() =>
                {
                    // Отключить поле гугл запроса
                    searchRequestTextBox.Enabled = false;

                    // Отключить чекбокс добавления в конец файла
                    appendToTempFileCheckBox.Enabled = false;

                    // Отключить кнопку выбора кол-ва страниц гугла
                    pagesCountComboBox.Enabled = false;

                    // Отключить кнопку поиска в гугле
                    findWebSitesButton.Enabled = false;
                    findWebSitesButton.BackColor = disabledBGColor;
                    findWebSitesButton.ForeColor = disabledTextColor;

                    // Отключить кнопку открытия временного файла
                    openWebPagesTempFileButton.Enabled = false;
                    openWebPagesTempFileButton.BackColor = disabledBGColor;
                    openWebPagesTempFileButton.ForeColor = disabledTextColor;

                    // Отключить кнопку удаления временного файла
                    deleteWebPagesTempFileButton.Enabled = false;
                    deleteWebPagesTempFileButton.BackColor = disabledBGColor;
                    deleteWebPagesTempFileButton.ForeColor = disabledTextColor;

                    // Отключить поле ввода страниц
                    pagesNamesTextBox.Enabled = false;

                    // Отключить кнопку кол-ва потоков
                    coresCountComboBox.Enabled = false;

                    // Отключить кнопку поиска имейлов
                    findEmailsButton.Enabled = false;
                    findEmailsButton.ForeColor = disabledTextColor;
                    findEmailsButton.BackColor = disabledBGColor;
                }));
            }
            else
            {
                this.Invoke((Action)(() =>
                {
                    // Включить поле гугл запроса
                    searchRequestTextBox.Enabled = true;

                    // Включить чекбокс добавления в конец файла
                    appendToTempFileCheckBox.Enabled = true;

                    // Включить кнопку выбора кол-ва страниц гугла
                    pagesCountComboBox.Enabled = true;

                    if (isAnyTextEnteredInRequest)
                    {
                        // Включить кнопку поиска в гугле
                        findWebSitesButton.Enabled = true;
                        findWebSitesButton.BackColor = normalColor;
                        findWebSitesButton.ForeColor = disabledBGColor;
                    }
                    else
                    {
                        // Отключить кнопку поиска в гугле
                        findWebSitesButton.Enabled = false;
                        findWebSitesButton.BackColor = disabledBGColor;
                        findWebSitesButton.ForeColor = disabledTextColor;
                    }

                    // Включить поле ввода страниц
                    pagesNamesTextBox.Enabled = true;

                    // Включить кнопку кол-ва потоков
                    coresCountComboBox.Enabled = true;



                    if (isTempFileExists)
                    {
                        // Включить кнопку открытия временного файла
                        openWebPagesTempFileButton.Enabled = true;
                        openWebPagesTempFileButton.BackColor = normalColor;
                        openWebPagesTempFileButton.ForeColor = disabledBGColor;

                        // Включить кнопку удаления временного файла
                        deleteWebPagesTempFileButton.Enabled = true;
                        deleteWebPagesTempFileButton.BackColor = redColor;
                        deleteWebPagesTempFileButton.ForeColor = disabledBGColor;

                        // Включить кнопку поиска имейлов
                        findEmailsButton.Enabled = true;
                        findEmailsButton.BackColor = normalColor;
                        findEmailsButton.ForeColor = disabledBGColor;
                    }
                    else
                    {
                        // Отключить кнопку открытия временного файла
                        openWebPagesTempFileButton.Enabled = false;
                        openWebPagesTempFileButton.BackColor = disabledBGColor;
                        openWebPagesTempFileButton.ForeColor = disabledTextColor;

                        // Отключить кнопку удаления временного файла
                        deleteWebPagesTempFileButton.Enabled = false;
                        deleteWebPagesTempFileButton.BackColor = disabledBGColor;
                        deleteWebPagesTempFileButton.ForeColor = disabledTextColor;

                        // Отключить кнопку поиска имейлов
                        findEmailsButton.Enabled = false;
                        findEmailsButton.ForeColor = disabledTextColor;
                        findEmailsButton.BackColor = disabledBGColor;
                    }
                }));

                return;
            }
        }




        public delegate void TaskComplited(Task task);
        public delegate void EmailSearch(object sender, EventArgs e);
    }
}
