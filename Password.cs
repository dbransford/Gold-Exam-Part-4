using System;
using System.Text;
using System.Collections.Generic;
using Crestron.SimplSharp.CrestronLogger; 
using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharp;                          				// For Basic SIMPL# Classes


namespace Password
{
    public delegate short CallUnlock(ushort AccessLevel);
    public delegate short UpdateUI();
    public delegate short UserDialogMessage(SimplSharpString Message);
    public class user : IComparable<user>
    {
        public string UserName;
        public string Password;
        public ushort AccessLevel;
        public string LastLogin = "";
        public user(string User, string Pass, ushort Level)
        {
            UserName = User;
            Password = Pass;
            AccessLevel = Level;
        }
        public user(user x)
        {
            UserName = x.UserName;
            Password = x.Password;
            AccessLevel = x.AccessLevel;
            LastLogin = x.LastLogin;
        }
        public int CompareTo(user x)
        {
            return this.UserName.CompareTo(x.UserName);
        }
    }
    ///////////////////////////////
    //Main class that handles the 
    //current user list, adds, 
    //removes, sorts, and does the unlock
    ///////////////////////////////
    public static class Login
    {
        //Data Array the has the user list durring run time
        public static List<user> Users = new List<user>();
        public static string[] UserNames = new string[100];
        public static string BackdoorUser = "";
        public static string BackdoorPassword = "";
        public static ushort BackdoorAccessLevel = 0;
        public static ushort FileRead = 0; //Semaphore to prevent the readdata() from only being called once at statup.
        public static string FilePath = "";
        public static UpdateUI SendUIUpdate { get; set; }
        public static UserDialogMessage SendDialogMessage { get; set; }
        public static ushort ListSize
        {
            get
            {
                return (ushort)Users.Count;
            }
        }
        public static bool IsAllDigits(string s)
        {
            foreach (char c in s)
            {
                if (!Char.IsDigit(c))
                    return false;
            }
            return true;
        }
        ///////////////////////////////
        // Add User Method
        //
        ////////////////////////////////
        static public void AddUser(string UserName, String PassCode,ushort Level)
        {
            
            if(!Users.Exists(x => x.UserName == UserName))
            {
                if (IsAllDigits(PassCode) && PassCode.Length > 5)
                {
                    string passhash = PassCode.GetHashCode().ToString(); //Hash the password      
                    Users.Add(new user(UserName, passhash, Level)); //Add new object to list. Pass parms to object constructor
                    Users.Sort(); //Request sort after an add
                    CrestronLogger.WriteToLog("User Added : " + UserName, 2);
                    WriteData(); //write current list to disk
                    SendUIUpdate();
                }
                else
                {
                    SendDialogMessage("Pascode must be six or more digits, and can only be numbers 0-9.");
                }
                
            }
            else
            {
                SendDialogMessage("User already exists. Please enter a unique name.");           
            }
        }
        ///////////////////////////////
        //Delete User Method
        //
        ///////////////////////////////
        static public void DeleteUser(string UserName)
        {
            if (Users.Remove(Users.Find(x => x.UserName == UserName)))//Remove user from list (Find User in list)
            {
                CrestronLogger.WriteToLog("User Deleted : " + UserName, 2);
                WriteData(); //if it found and removed a user write the data back to disk
                SendUIUpdate();
            }
            else
            {
                CrestronConsole.PrintLine("No User found");
            }
        }
        ///////////////////////////////
        //Update Passcode
        //
        ///////////////////////////////
        static public void UpdatePasscode(string UserName, string PassCode)
        {
            if (IsAllDigits(PassCode) && PassCode.Length > 5)
            {
                int index = Users.FindIndex(x => x.UserName == UserName);
                Users[index].Password = PassCode.GetHashCode().ToString();
                CrestronLogger.WriteToLog("User Passcode Updated : " + UserName, 2);
                WriteData();
            }
            else
            {
                SendDialogMessage("Pascode must be six or more digits, and can only be numbers 0-9.");
            }

        }
       
        
        static public ushort GetUserAccessLevel(string UserName)
        {
            //int index = Users.FindIndex(x => x.UserName == UserName);
            user z = new user(Users.Find(x => x.UserName == UserName));
            return z.AccessLevel;
        }
        
        
        static public string GetLastLogin(string UserName)
        {
            //int index = Users.FindIndex(x => x.UserName == UserName);
            user z = new user(Users.Find(x => x.UserName == UserName));
            return z.LastLogin;
        }
        
        ///////////////////////////////
        //Update Access
        //
        ///////////////////////////////
        static public void UpdateAccessLevel(string UserName, ushort Level)
        {
            int index = Users.FindIndex(x => x.UserName == UserName);
            Users[index].AccessLevel = Level;
            CrestronLogger.WriteToLog("User Access Level Updated : " + UserName, 2);
            WriteData();
            SendUIUpdate();
        }
        ///////////////////////////////
        //FILE FUNCTIONS
        //
        //Write to file
        //
        ///////////////////////////////
        public static void WriteData()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
            int z = 0;
            using (FileStream fs = File.Create(FilePath))
            {
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    foreach (user i in Users)
                    {
                        writer.WriteLine(i.UserName + "," + i.Password + "," + i.AccessLevel);
                        UserNames[z] = i.UserName;
                        z++;
                    }
                }
            }

                    
        }
        ///////////////////////////////
        //
        //Read from file
        //
        ///////////////////////////////
        public static void ReadData()
        {
            CrestronLogger.Initialize(5, LoggerModeEnum.DEFAULT);
            char[] delimiterChars = {','};
            List<string> DataLines = new List<string>();
            if (File.Exists(FilePath))
            {

                using (StreamReader SR = new StreamReader(FilePath))//Create Streamwriter
                    {
                        while(SR.EndOfStream != true)
                        {
                            DataLines.Add(SR.ReadLine()); 
                        }
                        int z = 0;
                        foreach (string i in DataLines)
                        {
                            string[] line = i.Split(delimiterChars);
                            Users.Add(new user(line[0], line[1], Convert.ToUInt16(line[2])));
                            UserNames[z] = line[0];
                            z++;
                        }
                        CrestronLogger.WriteToLog("User data loaded.",2);
                        SR.Close();
                        
                    }


                SendUIUpdate();    
            }
            FileRead = 1;

        }
        public static void ForceUIUpdate()
        {
            SendUIUpdate();
        }

        ///////////////////////////////
        // Method for S+ to send passcode
        //for unlock
        ///////////////////////////////
        static public ushort SendLogin(string password,ref InterfaceInstance x) //Must pass passcode, and its object reference
        {
            ushort ReturnValue = 0;
            /*if (x.InterfaceLocked)
            {
                x.DialogMessage("Too many login attempts. Interface Locked.");
                return 0;
            }
            else*/
            {
                if (password == BackdoorPassword)
                {
                    x.unlock(BackdoorAccessLevel);
                    //z.LastLogin = DateTime.Now + " at " + x.LocationName;
                    CrestronConsole.PrintLine(BackdoorUser + " Unlocked System");
                    CrestronLogger.WriteToLog(BackdoorUser + " Unlocked System From " + x.LocationName, 2);
                    ReturnValue = 1;
                }
                else
                {
                    foreach (user z in Users)
                    {
                        if (z.Password == password.GetHashCode().ToString()) //Hash passcode and check
                        {
                            x.unlock(z.AccessLevel);
                            z.LastLogin = DateTime.Now + " at " + x.LocationName;
                            CrestronConsole.PrintLine(z.UserName + " Unlocked System");
                            CrestronLogger.WriteToLog(z.UserName + " Unlocked System From " + x.LocationName, 2);
                            ReturnValue = 1;
                        }
                    }
                }
                if (ReturnValue == 0)
                {
                    //CrestronConsole.PrintLine("No User found");
                    x.DialogMessage("Invalid Passcode. Please try again");
                    //x.LoginAttempts++;
                    //if (x.LoginAttempts >= 3)
                    //{
                    //    x.LockInterface();  

                    //}

                    CrestronLogger.WriteToLog("Failed loggin attempt at " + x.LocationName, 2);
                }
                return ReturnValue;
            }
        }

        //Debug Output
        static public void PrintUsers()
        {
            foreach (user i in Users)
            {
                CrestronConsole.PrintLine(i.UserName + "\t" + i.Password + "\t" + i.AccessLevel);
            }
        }
    }

    public class InterfaceInstance
    {
        public CallUnlock CallbackUnlock { get; set; }
        public UserDialogMessage DialogMessage { get; set; }
        public ushort AccessLevel = 0;
        public string LocationName = "";
        public int LoginAttempts = 0;
        public bool InterfaceLocked = false;
        public void unlock(ushort level)
        {
            CallbackUnlock(level);
        }

        public void LockInterface()
        {
            InterfaceLocked = true;
            CTimer Timer = new CTimer(LockTimer,60000);
            
        }
        public void LockTimer(object unused)
        {
            InterfaceLocked = false;
            LoginAttempts = 0;
        }

    }

}
