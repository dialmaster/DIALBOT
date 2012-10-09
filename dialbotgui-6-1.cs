/* DialBOT in a GUI! */

using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using System.Text;
using System.Linq;
using System.Web;
using Gtk;
 
// TODO: Top 10 ranked submitters (by rank)
// TODO: Top 10 messages by score


public class DialBOT : Window {

    static string PING = "PING :";
    // Irc server to connect
    public static string SERVER = "irc.nogoodshits.net";
    //public static string SERVER = "j3b.us";

    // Irc server's port (6667 is default port)
    private static int PORT = 6667;
    // User information defined in RFC 2812 (Internet Relay Chat: Client Protocol) is sent to irc server
    private static string USER = "USER DialBot 8 * :I'm a crappy C# irc bot";
    // Bot's nickname
    private static string NICK = "DialBOT";
    // Channel to join
    private static string CHANNEL = "#nogoodshits";
    //private static string CHANNEL = "#dialshits";

    // StreamWriter is declared here so that PingSender can access it
    public static StreamWriter writer;
    private static Random random = new Random();

    /* All the possible balls-deep responses. Since these will be up and downvoteable they need to have a SCORE associated to them.
     *  Top level dictionary key is the balls-deep response. Second level contains NICK as key and VOTE as the int (either 2,1 or -1)
     * Total score is the total of all votes. The FIRST person to put a new entry in gets a vote of 2. This also allows you to 
     * distinguish WHO put the entry in.
     * This will simply be stored tab-delimited as RESPONSE\tUSER\t\VOTE\tUSER\tVOTE\t  ect... */
    private static Dictionary<String, Dictionary<String, Int16>> ballsDeep = new Dictionary<String, Dictionary<String, Int16>>();
    private static String lastBall = "";
    private static Boolean cancelLOUD = false;


    private static Statusbar statusbar;
    private static int count = 0;
    private static bool isRunning = true;
    private static Button startstop; 
    private static String consoletext = "Console text goes here\n";
    private static TextBuffer consolebuffer;
    private static TextView consoleview;

    private static NetworkStream stream;
    private static TcpClient irc;
    private static string inputLine;
    private static StreamReader reader;
    private static CheckButton followConsole;
    private static TreeStore bdstore;
    private static Entry filterEntry;
    private static TreeModelFilter filter;
    private static TreeModelSort bdsorted;
    private static TreeView ballsdeeptree;

    private static int deathTimer=16000; // Kill DialBOT every so often so that he stays in channel.
                                         // This requires that the perl program autorestart wraps this....

    public DialBOT() : base("DialBOT") {
        SetDefaultSize(800, 600);
        SetPosition(WindowPosition.Center);
        DeleteEvent += delegate { Application.Quit(); };

        // Top level container. This vertically divides the window into 3 sections:
        // Top is the 2 windows (balls-deep items and console output        
        VBox topvbox = new VBox(false, 5);

        // Lets build the top 1/3rd here.
        Frame ballsdeepframe = new Frame("Balls-Deep Items");       
        Frame consoleframe = new Frame("Console Output");
        // HBox mainhbox = new HBox(true,2);

        ballsdeeptree = new TreeView();

        bdstore = new TreeStore (typeof(string), typeof (string), typeof (string), typeof (string));
        LoadBalls();
        fillStore();


        // Put the TreeStore into a TreeModelSort so we can sort columns...        
        // Sort by score for now
        bdsorted = new TreeModelSort(bdstore);
        bdsorted.SetSortColumnId(1,SortType.Descending);        

        // Put the TreeModelSort into a TreeModelFilter so we can implement the filtering
        filterEntry = new Entry();
        // filter = new TreeModelFilter(bdsorted,null);
        // filter.VisibleFunc = FilterTreeFunc;   // Use this as the filter function.

        // And then set the visible TreeView to use the filter as it's store.
        ballsdeeptree.Model = bdsorted;

        ballsdeeptree.HeadersVisible = true;
        ballsdeeptree.HeadersClickable=true;        
        ballsdeeptree.AppendColumn ("Added By", new CellRendererText (), "text", 0);
        ballsdeeptree.AppendColumn ("Score", new CellRendererText (), "text", 1);
        CellRendererText ballRenderer = new CellRendererText();        
        ballsdeeptree.AppendColumn ("Text", ballRenderer, "text", 2);
        CellRendererText voteRenderer = new CellRendererText();        
        voteRenderer.Editable=true;
        voteRenderer.Edited+=editVotes;
        ballsdeeptree.AppendColumn ("Votes", voteRenderer, "text", 3);
  
        TreeViewColumn col = ballsdeeptree.GetColumn(0);
        col.Clickable=true;
        col.Resizable = true;
        col.Clicked += new EventHandler (col_clicked0);        

        col = ballsdeeptree.GetColumn(1);
        col.Resizable = true;
        col.Clickable=true;
        col.Clicked += new EventHandler (col_clicked1);        

        col = ballsdeeptree.GetColumn(2);
        col.Resizable = true;
        col.Clickable=true;
        col.Clicked += new EventHandler (col_clicked2);        

        col = ballsdeeptree.GetColumn(3);
        col.Clickable=true;
        col.Resizable = true;
        col.Clicked += new EventHandler (col_clicked3);        



        ScrolledWindow ballsdeepscroll = new ScrolledWindow();

        ballsdeepscroll.Add(ballsdeeptree);

        Button deleteentry = new Button("Remove Entry");
        deleteentry.SetSizeRequest(70, 30);
        deleteentry.Clicked += new EventHandler(deleteBallMsg);  

        ballsdeepframe.Add(ballsdeepscroll);
        ballsdeepframe.Add(deleteentry);

        // Entry box and label to filter on message as well as an HBox to put them next to each other
        filterEntry.Changed += OnFilterEntryTextChanged;
        Label filterLabel = new Label("Ball Message Search: ");
        HBox filterBox = new HBox();
        filterBox.PackStart(filterLabel,false,false,5);
        filterBox.PackStart(filterEntry,true,true,5);


        VBox ballvbox = new VBox(false,5);
        ballvbox.Add(ballsdeepframe);
        //ballvbox.PackStart(filterBox,false,false,1);        

        topvbox.Add(ballvbox);

        consoleview = new TextView();
        consolebuffer=consoleview.Buffer;
        consolebuffer.Text = consoletext;

        ScrolledWindow consolescroll = new ScrolledWindow();
        consolescroll.SetPolicy(PolicyType.Automatic,PolicyType.Always);
        consolescroll.Add(consoleview);

        consoleframe.Add(consolescroll);

        followConsole = new CheckButton("Tail Console");
        followConsole.SetSizeRequest(70,30);

        VBox consolevbox = new VBox(false,5);
        consolevbox.Add(consoleframe);
        consolevbox.PackStart(followConsole,false,false,1);

        topvbox.Add(consolevbox);

        //        topvbox.PackStart(mainhbox, true,true,4);

        // Now the 2nd 3rd. This contains 2 buttons. A start/stop, and a close.
        HBox buttonhbox = new HBox(true, 3);
        startstop = new Button("Stop");
        startstop.SetSizeRequest(70, 30);
        startstop.Clicked += new EventHandler(startstopEvent);        
        Button close = new Button("Close");
        close.Clicked += new EventHandler(quitEvent);

        buttonhbox.Add(startstop);
        buttonhbox.Add(close);

        Alignment halign = new Alignment(1, 0, 0, 0);
        halign.Add(buttonhbox);

        topvbox.PackStart(halign, false, false, 3);

        // Now the bottom 3rd. A status bar
        statusbar = new Statusbar();
        statusbar.Push(1,"Hey, it's a status");
        topvbox.PackStart(statusbar,false,false,0);

        // Add our top level container to the window
        Add(topvbox);

        ShowAll();
    }

    public static void deleteBallMsg(object obj, EventArgs args) {
        // Find out what they have selected

        // Remove from bdstore

        // Remove from the underlying data model

        // Saveballs

    }

    // What to do when someone edits the underlying ball vote text. Once again the data DialBot uses
    // must be updated from the model
    public static void editVotes(object o, EditedArgs args) {
        // New text comes in in args.NewText
        TreeIter iter;
        TreePath startPath =  new TreePath(args.Path.ToString());
        TreePath convPath = bdsorted.ConvertPathToChildPath(startPath);
        bdstore.GetIter(out iter, convPath);
        Console.WriteLine("The value of the cell is currently " + bdstore.GetValue(iter,3).ToString() +
"and we are going to write " + args.NewText + " to it.");

        // Update vote string in UI/model for the ui
        bdstore.SetValue(iter,3,args.NewText);
    
        // Pass in: ballmsg, new votestring, and who added it
        // Update vote Dictionary in the underlying data structure
        String thisBallMsg = bdstore.GetValue(iter,2).ToString();
        modifyBallVotes(thisBallMsg, args.NewText, bdstore.GetValue(iter,0).ToString());

        // Recalc score from underlying data
        Int16 totalvotes = getBallScore(thisBallMsg).First().Value;
        String score = totalvotes.ToString();
        
        String newVoteString = getVoteString(thisBallMsg);

        Console.WriteLine("Underlying data after update: " + newVoteString);

        // Set new score in the UI
        bdstore.SetValue(iter,1,score);
        
        // And SaveBalls to disks!
        SaveBalls();
    }
    


    public static SortType SetSortOrder (TreeViewColumn col)    {
        if (col.SortIndicator)  {
            if (col.SortOrder == SortType.Ascending)
                return SortType.Descending;
            else return SortType.Ascending;
        }
        else return SortType.Ascending;
    }

    private static void col_clicked0 (object o, EventArgs args)    {
        TreeViewColumn col = (TreeViewColumn) o;
        col.SortOrder = SetSortOrder (col); // set order asc or desc
        col.SortIndicator = true; // turn on sort indicator
        bdsorted.SetSortColumnId (0, col.SortOrder);
    }
    private static void col_clicked1 (object o, EventArgs args)    {
        TreeViewColumn col = (TreeViewColumn) o;
        col.SortOrder = SetSortOrder (col); // set order asc or desc
        col.SortIndicator = true; // turn on sort indicator
        bdsorted.SetSortColumnId (1, col.SortOrder);
    }
    private static void col_clicked2 (object o, EventArgs args)    {
        TreeViewColumn col = (TreeViewColumn) o;
        col.SortOrder = SetSortOrder (col); // set order asc or desc
        col.SortIndicator = true; // turn on sort indicator
        bdsorted.SetSortColumnId (2, col.SortOrder);
    }
    private static void col_clicked3 (object o, EventArgs args)    {
        TreeViewColumn col = (TreeViewColumn) o;
        col.SortOrder = SetSortOrder (col); // set order asc or desc
        col.SortIndicator = true; // turn on sort indicator
        bdsorted.SetSortColumnId (3, col.SortOrder);
    }





    private void OnFilterEntryTextChanged(object o, System.EventArgs args) {
        filter.Refilter();    
    }

    bool FilterTreeFunc(TreeModel model, TreeIter iter) {
        String ballsmessage = model.GetValue(iter,2).ToString();
        if (filterEntry.Text == "") {
            return true;
        } 
        if (ballsmessage.IndexOf(filterEntry.Text) > -1) {
            return true;
        } else {
            return false;
        }
            
    }

    public static void Main() {
        Application.Init();
        new DialBOT();
        Connect();
        GLib.Timeout.Add(20, new GLib.TimeoutHandler(Update));
        GLib.Timeout.Add(18000, new GLib.TimeoutHandler(Ping));
        Application.Run();


    }

    static public void Connect() {
        addToConsole("Connecting to IRC...");
        irc = new TcpClient(SERVER, PORT);
        stream = irc.GetStream();
        reader = new StreamReader(stream);
        writer = new StreamWriter(stream);
        writer.WriteLine(USER);
        writer.Flush();
        writer.WriteLine("NICK " + NICK);
        writer.Flush();
        // For ngs
        writer.WriteLine("JOIN " + CHANNEL + " monkeyspoon");
        writer.Flush();
        addToConsole("Connected.");
    }

    static bool Ping() {
        deathTimer-=15;
        addToConsole("Ping sent! Total time is " + deathTimer);        
        if (deathTimer < 0) { System.Environment.Exit(0); }
        try {
            writer.WriteLine(PING + SERVER);
            writer.Flush();    
        } catch (System.Exception ex) {    
            addToConsole("Exception in PING: "+ex);            
            Connect();
            return false;
        }
        return true;    
    }

    static void addToConsole(String more) {
        consoletext += more + "\n";        
        consolebuffer.Text = consoletext;    
        if (followConsole.Active) {
            consoleview.ScrollToIter(consolebuffer.EndIter,0.49,true,0.0,0.0);    
        }
    }


    static bool Update() {
        try {        
        if (isRunning) { 
            string message = String.Format("Running...", count); // No real reason for this. Just wanted a status bar
            statusbar.Push(1,message);
                if (stream.DataAvailable) { inputLine = reader.ReadLine(); } else { inputLine=null;}
                if (inputLine != null) {
                    addToConsole(inputLine);                    
                    Dictionary<String, String> input = parseLine(inputLine);
                    if (input.ContainsKey("Type")) {  // Don't try to respond if we didn't get back an input type we recognize
                        String outmsg = "";
                        if (input["Type"].Equals("KICK")) {
                            Thread.Sleep(1000);
                            outmsg = "JOIN " + CHANNEL + " monkeyspoon";
                        }
                        if (input["Type"].Equals("JOIN") && (int)random.Next(0, 3) == 1) {
                            outmsg = Greeting(input["User"]);
                        }
                        if (input["Type"].Equals("CHANNEL")) {
                            if (input["Message"].ToUpper().StartsWith(NICK.ToUpper())) {
                                outmsg = ChanResponsesToMe(input["Message"], input["User"]);
                            } else {
                                outmsg = ChanResponsesGeneral(input["Message"], input["User"]);
                            }
                        }
                        if (input["Type"].Equals("PRIVATE")) {
                            outmsg = PrivResponses(input["Message"], input["User"]);
                        }
                        if (outmsg.Length > 0) {
                            if (outmsg.Equals("timetodie")) {
                                addToConsole("Writing to IRC: As you wish " + input["User"]);
                                writer.WriteLine("PRIVMSG " + CHANNEL + " :As you wish " + input["User"] + ".");
                                writer.WriteLine("QUIT");

                                writer.Flush();
                                Thread.Sleep(2000);

                                // Close all streams
                                writer.Close();
                                reader.Close();
                                irc.Close();
                                SaveBalls();
                                Environment.Exit(0);
                            }
                            addToConsole("Writing to IRC: " + outmsg);
                            writer.WriteLine(outmsg);
                            writer.Flush();
                            // Sleep to prevent excess flood
                            Thread.Sleep(1000);
                        }
                    }
                }
        }
        } catch (Exception e) {
            // Show the exception, sleep for a while and try to establish a new connection to irc server
            Console.WriteLine(e.ToString());

            // Just re-running Connect() did not work... Let's try it this way.
            Thread.Sleep(15000);
            Connect();
            //Main();
        }        
        return true;
    }

    static void startstopEvent(object obj, EventArgs args) {
        if (isRunning) {
            startstop.Label = "Start";            
            isRunning = false;
        } else {
            isRunning = true;
            startstop.Label = "Stop";
        }    
    }

    static void quitEvent(object obj, EventArgs args) {
        Application.Quit();        
        System.Environment.Exit(0);    
    } 


    static private Dictionary<String, String> parseLine(String inputLine) {
        Dictionary<String, String> returnvals = new Dictionary<String, String>();
        // Who posted this?
        Regex whoFrom = new Regex(@"^\:[\w\d\\|`'^{}\]\[-]+?\!");
        Match whoMatch = whoFrom.Match(inputLine);
        if (whoMatch.Success) {
            String userName = whoMatch.Value.Substring(1, whoMatch.Value.Length - 2);
            if (userName.Equals(NICK)) { return returnvals; } // Return nothing if it was myself :)
            returnvals["User"] = userName;
        }

        // Check for PART or JOIN. No need for additional info here, so just return
        Regex isPart = new Regex(@"^\S+\sPART");
        if (isPart.IsMatch(inputLine)) {
            returnvals["Type"] = "PART";
            return returnvals;
        }
        Regex isKick = new Regex(@"^\S+\sKICK");
        if (isKick.IsMatch(inputLine)) {
            returnvals["Type"] = "KICK";
            return returnvals;
        }

        Regex isJoin = new Regex(@"^\S+\sJOIN");
        if (isJoin.IsMatch(inputLine)) {
            returnvals["Type"] = "JOIN";
            return returnvals;
        }


        // If someone said something, get ths info on it.
        Regex isChanMSG = new Regex(@"PRIVMSG\s\#.*$");
        Match chanMatch = isChanMSG.Match(inputLine);

        Regex isPrivMSG = new Regex(@"PRIVMSG\s\w.*$");
        Match privMatch = isPrivMSG.Match(inputLine);
        String theMessage;

        if (chanMatch.Success) {
            theMessage = chanMatch.Value;
            returnvals["Type"] = "CHANNEL";
        } else if (privMatch.Success) { // If it's not a channel match, check and see if it's a private message
            theMessage = privMatch.Value;
            returnvals["Type"] = "PRIVATE";
        } else {
            // Whatever the input was, it wasn't a PART, JOIN, PRIVATE or IN CHANNEL chat, so just return with no Type
            return returnvals;
        }

        // Everything UP TO the first : is not part of the message, drop it!
        String message = theMessage.Substring(theMessage.IndexOf(':') + 1);
        returnvals["Message"] = message;
        if (returnvals["Type"].Equals("CHANNEL")) { addToConsole("Channel message for "+returnvals["User"] + " was: " + message); }
        if (returnvals["Type"].Equals("PRIVATE")) { addToConsole("Private message for " + returnvals["User"] + " was: " + message); }
        if (returnvals["Type"].Equals("JOIN")) { addToConsole("Join message for " + returnvals["User"] + " was: " + message); }
        if (returnvals["Type"].Equals("PART")) { addToConsole("Part message for " + returnvals["User"] + " was: " + message); }
        if (returnvals["Type"].Equals("KICK")) { addToConsole("Kick message for " + returnvals["User"] + " was: " + message); }
        
        return returnvals;
    }

    // Used for adding, subtracting, up and downvoting. A given nick can only add or vote ONCE, but they can SWITCH their vote if they wish
    static private String ballVote(String ballmsg, Int16 vote, String username) {
        String ballRet = "";

        // No message? Just return.
        if (ballmsg.Equals("")) { return ""; }

        // If message already existed
        if (ballsDeep.ContainsKey(ballmsg)) {
            if (vote == 2) {
                return username + ": You cannot ADD this to my list of things to be balls-deep in. It's already there.";
            }
            if (ballsDeep[ballmsg].ContainsKey(username)) { // If this username already had a vote for that
                if (ballsDeep[ballmsg][username] == 2) {
                    return username + ": No voting for your own messages.";
                }
                if (vote != ballsDeep[ballmsg][username]) { // User is CHANGING their vote
                    ballsDeep[ballmsg][username] = vote;
                    if (vote == 1) {
                        ballRet = username + ": Changing your vote from a downvote to an upvote.";
                    } else if (vote == -1) {
                        ballRet = username + ": Changing your vote from an upvote to a downvote.";
                        if (getBallScore(ballmsg).First().Value <= -1) { ballRet = "Due to downvotes, you will no longer find me balls-deep in " + ballmsg + "."; }
                    }
                } else {// user is trying to vote the same
                    ballRet = username + ": Yeah man... whatever.";
                }
            } else { // This is a NEW vote for this by this user.
                ballsDeep[ballmsg][username] = vote;
                if (getBallScore(ballmsg).First().Value <= -2) { ballRet = "Due to downvotes, you will no longer find me balls-deep in " + ballmsg + "."; }
        ballRet = "";
            }
        } else { // Message did NOT exist, we are ADDING it with this user and 2 votes.
            Dictionary<String, Int16> addMsg = new Dictionary<String, Int16>();
            addMsg[username] = 2;
            ballsDeep[ballmsg] = addMsg;
            ballRet = username + ": You will now occasionally find me balls-deep in " + ballmsg + ".";
        }

        SaveBalls();
        updateStore(ballmsg);

        return ballRet;
    }

    static private String whoWins() {
        Dictionary<String, Int16> votesByUser = new Dictionary<String, Int16>();
    
        // Loop through all entries in the ballsdeep, 
        int highest = -1000;
        foreach (String ballmsg in ballsDeep.Keys) {
            Int16 totalvotes = getBallScore(ballmsg).First().Value;
            String user = getBallScore(ballmsg).First().Key;
            if (votesByUser.Keys.Contains(user)) {
                votesByUser[user] += totalvotes;
            } else {
                votesByUser[user] = totalvotes;
            }
        }
        List<String> winningUsers = new List<String>();
        foreach (String thisuser in votesByUser.Keys) {
            if (votesByUser[thisuser] > highest) {
                winningUsers.Clear();
                winningUsers.Add(thisuser);
                highest=votesByUser[thisuser];
            }
            if (votesByUser[thisuser] == highest && !winningUsers.Contains(thisuser)) {
                winningUsers.Add(thisuser);
                highest=votesByUser[thisuser];
            }
        }

        if (winningUsers.Count == 1) {
            return winningUsers[0] + " wins with a rep of " + highest.ToString();
        }
        if (winningUsers.Count == 2) {
            return "We have a tie! Both " + winningUsers[0] + " and " + winningUsers[1] + " win with a score of " + highest.ToString();
        }
        if (winningUsers.Count > 2) {
            return "There are more than 2 people with the same 'top' score. No one wins.";
        }

        return "";
    }


    // Return a simple string array of all the *active* (totalvotes >-2) entries
    static public List<String> getActiveBalls() {
        List<String> activeBalls = new List<String>();

        foreach (String ballmsg in ballsDeep.Keys) {
            int totalvotes = getBallScore(ballmsg).First().Value;
            if (totalvotes > -2) { activeBalls.Add(ballmsg); }
        }

        return activeBalls;
    }

    static public String getVoteString(String thisBallMsg) {
        String retval = String.Empty;
        foreach (KeyValuePair<String, Int16> innerEntry in ballsDeep[thisBallMsg]) {
        if (innerEntry.Value < 2) { 
            retval += innerEntry.Key + " = " + innerEntry.Value + "; "; 
        }
        }
        
        return retval;
    }

    static public void modifyBallVotes(String thisBallMsg, String theseBallVotes, String whoadded) {

        // Ripped from LoadBalls. 2 things to note here:
        // 1) The ballstring getting passed is from theUI and looks like:
        //dialman=1; kidlazarus=-1;gaziel=1;
        // So we need to parse that and 2: The person who submitted the ballmsg is NOT going
        // to be in that string, but needs to be in the new dictionary with 2 votes.    
        String[] splitLine = theseBallVotes.Split(';');
        Dictionary<String, Int16> innerDict = new Dictionary<String, Int16>();
        for (int i = 0; i < splitLine.Count()-1; i += 1) {
            String thisVote = splitLine[i];  // This looks like dialman = 1.There may be a leading or trailing space
            String[] splitVote = thisVote.Split('='); 
            String myUser = splitVote[0]; // User name with leading/trailing space
            String myVote = splitVote[1]; // User vote (-1 or 1) with leading/trailing space

            innerDict[myUser.Trim()] = Convert.ToInt16(myVote);
        }
        innerDict[whoadded] = 2;
        ballsDeep[thisBallMsg] = innerDict;

    }


    static public Dictionary<String,Int16> getBallScore(String thisBallMsg) {
        Dictionary<String, Int16> ballInfo = new Dictionary<String, Int16>();

        // Stupid cheaters. This can't STOP it, but this way only approved nicks can have votes that count.
        Dictionary<String, Int16> approvedVoters = new Dictionary<String, Int16>()
        {
            {"dialman", 1},
            {"Dybbuk", 1},
            {"gaziel", 1},
            {"gloin", 1},
            {"sam", 1},
            {"qbit", 1},
            {"Doc", 1},
            {"bobcrotch", 1},
            {"aaron", 1},
            {"chelsea", 1},
            {"hoodoo", 1},
            {"KidLazarus", 1},
            {"PopeKetric", 1},
            {"Doogles", 1},
            {"Nerdmaster", 1},
            {"telzey", 1}
        };
        
        
        String whoadded = "";
        Int16 totalvotes = 0;
        if (!ballsDeep.ContainsKey(thisBallMsg)) { return ballInfo; }
        foreach (KeyValuePair<String, Int16> innerEntry in ballsDeep[thisBallMsg]) {
            if (innerEntry.Value == 2) {
                whoadded = innerEntry.Key;
            } else {
                if (innerEntry.Value < 2) { 
                    if (approvedVoters.ContainsKey(innerEntry.Key)) { totalvotes += innerEntry.Value; }
                } else { 
                    totalvotes += innerEntry.Value; totalvotes -= 1; 
                }
            }

        }
        ballInfo[whoadded] = totalvotes;
        return ballInfo;
    }

    static public int getBallRep(String username) {
        Int16 totalvotes = 0;
        foreach (String ballmsg in ballsDeep.Keys) {
            if (getBallScore(ballmsg).First().Key.Equals(username)) {
                totalvotes += getBallScore(ballmsg).First().Value;
            }
        }
        if (username.Equals("dialman")) {
            totalvotes -= 10;
        }
        return totalvotes;
    }

    // What we really want here is to parse what was SAID to me and do or say something relevant. Use some randomness to mix it up
    static private String ChanResponsesToMe(String input, String username) {
        String outmsg = "";
        int choice = random.Next(0, 30);

        if (username.Equals("dialman") || username.Equals("dialy") || username.Equals("dialmaster")) {
            Regex isKill = new Regex(@"die\!$", RegexOptions.IgnoreCase);
            Match killMatch = isKill.Match(input);

            if (killMatch.Success) {
                return "timetodie";
            }

        }
        Regex iswhoWins = new Regex(@"who wins\?$", RegexOptions.IgnoreCase);
        Match iswhoWinsMatch = iswhoWins.Match(input);

        Regex isHelp = new Regex(@"help$", RegexOptions.IgnoreCase);
        Match isHelpMatch = isHelp.Match(input);
        Regex isRoll = new Regex(@"roll\s\d+d\d+", RegexOptions.IgnoreCase);
        Match isRollMatch = isRoll.Match(input);
        Regex isBallsDeep = new Regex(@"balls\-deep", RegexOptions.IgnoreCase);
        Match isBallsDeepMatch = isBallsDeep.Match(input);
        if (iswhoWinsMatch.Success) {
            return "PRIVMSG " + CHANNEL + " :" + whoWins();
        }


        if (isBallsDeepMatch.Success) {
            choice = random.Next(0, 6);
            List<String> activeBalls = getActiveBalls();
            lastBall = activeBalls.ElementAt(random.Next(0, activeBalls.Count)).ToString();
            switch (choice) {
                case 0:
                    outmsg = "PRIVMSG " + CHANNEL + " :"+username +": Have you ever gone balls-deep in " + lastBall + "? It's wonderful!";
                    break;
                case 1:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Oh yeah. I am balls-deep in " + lastBall + "!";
                    break;
                case 2:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": My mother always told me \"Go balls-deep in " + lastBall + "\".";
                    break;
                case 3:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Go balls-deep in " + lastBall + "!";
                    break;
                case 4:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": I am currently balls-deep in " + lastBall + "!";
                    break;
                default:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": I love it when I am balls-deep in " + lastBall + "!";
                    break;


            }
            return outmsg;
        
        }


        if (isRollMatch.Success) {
            // Get dice info:
            String[] rollInfo = isRollMatch.Value.Split('d');

            int total = 0;
            if (rollInfo[0].Substring(5).Length > 4) { return "PRIVMSG " + CHANNEL + " :" + username + ":I don't have that many dice you fuckface."; }
            if (rollInfo[1].Length > 3) { return "PRIVMSG " + CHANNEL + " :" + username + ":The biggest die I have is 999 sided."; }

            int numRolls = Convert.ToInt16(rollInfo[0].Substring(5));

            int diceSides = Convert.ToInt16(rollInfo[1]);
            while (numRolls > 0) {
                total += random.Next(1, diceSides + 1);
                numRolls -= 1;
            }
            return "PRIVMSG " + CHANNEL + " :" + username + ": You rolled " + total.ToString() + ".";
        }

        if (isHelpMatch.Success) {
            return "PRIVMSG " + CHANNEL + " :I respond to these commands: 'DialBOT ROLL #d#', !WEATHER <ZIPCODE>, !STOCK <STOCK SYMBOL>, !NEWS <SUBREDDIT>, !BD, !UPBALL, !DOWNBALL, !BDREP, !BDSCORE and !BDHISTORY.";
        }


        switch (choice) {
            case 0:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Sounds like you could use a little R&R. Rum and Ritalin.";
                break;
            case 1:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Without Germans, you wouldn't have any of the Indiana Jones movies.";
                break;
            case 2:
                outmsg = "PRIVMSG " + CHANNEL + " :I'm not afraid of you, " + username + ". Let's do this.";
                break;
            case 3:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Do you have access to horse semen?";
                break;
            case 4:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Don't talk to me like that. You look like a turtle who lost his shell.";
                break;
            case 5:
                outmsg = "PRIVMSG " + CHANNEL + " :Boy! We as a group, might not smell great.";
                break;
            case 6:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Oh, we're going to have fun. We're going to stay here and make nachos and see who can fall asleep the earliest! Fun, fun, fun, fun!";
                break;
            case 7:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Well, that would only be a problem if I had any flaws.";
                break;
            case 8:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Nope. Hipster nonsense, I'm out.";
                break;
            case 9:
                outmsg = "PRIVMSG " + CHANNEL + " :And now a reading from Corinthians. 'Love is patient. Love is kind. It is not jealous. It is not pompous. It does not envy. It does not boast. It is not proud. It is not rude.'";
                break;
            case 10:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": And that is why you are so amazing!";
                break;
            case 11:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Wow. You do have a talent.";  
                break;
            case 12:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Okay, in my defense, every April 22nd I honor Richard Nixon's death by getting drunk and making some unpopular decisions.";
                break;
            case 13:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Weird in a good way, huh. Like going to the gym drunk.";
                break;
            case 14:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Let's go find a Canadian who will take our money.";
                break;
            case 15:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": You look like a prison weed dealer.";
                break;
            case 16:
                outmsg = "PRIVMSG " + CHANNEL + " :The secret to a strong healthy head of hair is dove... blood.";
                break;
            case 17:
                outmsg = "PRIVMSG " + CHANNEL + " :What are you talking about? Everyone loved your little " + username + " party. Nothing brings a team together like a harrowing experience. You pulled it off.";
                break;
            case 18:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": So Here's some advice I wish I would've got when I was your age: Live every week, like it's 'shark week'.";
                break;
            case 19:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Why don't you shut your mouth, back that ass up, and make me a sandwich.";
                break;
            case 20:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Are you a large child or a small adult?";
                break;
            case 21:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Great, I'll be in touch. You still using your Hotmail account?";
                break;
            case 22:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": I always knew this would end someday. I just thought it would be with me in the trunk of a rental car.";
                break;
            case 23:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Stop falling in love with gay guys?";
                break;
            case 24:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Excuse me. My friend has to go strangle her Anxiety Pillow.";
                break;
            case 25:
                outmsg = "PRIVMSG " + CHANNEL + " :Oh, great day, everyone. You guys are the real stars. ";
                break;
            case 26:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ":  	Wait... what? I was making my thing up. You bitch.";
                break;

            default:
                List<String> activeBalls = getActiveBalls();
                lastBall = activeBalls.ElementAt(random.Next(0, activeBalls.Count)).ToString();
                outmsg = "PRIVMSG " + CHANNEL + " :Sorry, " + username + ", I am balls-deep in " + lastBall + " right now.";
                break;
        }


        return outmsg;
    }

    // We probably don't want to generally respond to stuff said in channel unless it is something directly relevant. 
    // Definitely want some randomness in here, don't want to get too repetitive
    // botcheck responses should go here
    static private String ChanResponsesGeneral(String input, String username) {
        String outmsg = "";
        int choice = random.Next(0, 3);
        
        Regex isBallsDeep = new Regex(@"balls\-deep", RegexOptions.IgnoreCase);
        Regex isWeather = new Regex(@"^\!Weather\s\d\d\d\d\d", RegexOptions.IgnoreCase);
        Regex isBot = new Regex(@"^botcheck", RegexOptions.IgnoreCase);
        Regex isNews = new Regex(@"^\!News.*$", RegexOptions.IgnoreCase);
        Regex isStock = new Regex(@"^\!Stock\s\w+", RegexOptions.IgnoreCase);
        Regex isAddBall = new Regex(@"^\!bd\s.*$", RegexOptions.IgnoreCase);
        Regex isUpBall = new Regex(@"^\!upball", RegexOptions.IgnoreCase);
        Regex isDownBall = new Regex(@"^\!downball", RegexOptions.IgnoreCase);
        Regex isBallScore = new Regex(@"^\!bdscore", RegexOptions.IgnoreCase);
        Regex isBallHistory = new Regex(@"^\!bdhistory", RegexOptions.IgnoreCase);
        Regex isBallRep = new Regex(@"^\!bdrep.*$", RegexOptions.IgnoreCase);

        Match isBallsDeepMatch = isBallsDeep.Match(input);
        Match isBallRepMatch = isBallRep.Match(input);
        Match weatherMatch = isWeather.Match(input);
        Match botMatch = isBot.Match(input);
        Match newsMatch = isNews.Match(input);
        Match stockMatch = isStock.Match(input);
        Match isAddBallMatch = isAddBall.Match(input);
        Match isUpBallMatch = isUpBall.Match(input);
        Match isDownBallMatch = isDownBall.Match(input);
        Match isBallScoreMatch = isBallScore.Match(input);
        Match isBallHistoryMatch = isBallHistory.Match(input);


        if (isBallsDeepMatch.Success) {
            choice = random.Next(0, 6);
            List<String> activeBalls = getActiveBalls();
            lastBall = activeBalls.ElementAt(random.Next(0, activeBalls.Count)).ToString();

            switch (choice) {
                case 0:
                    outmsg = "PRIVMSG " + CHANNEL + " :Have you ever gone balls-deep in " + lastBall + "? It's wonderful!";
                    break;
                case 1:
                    outmsg = "PRIVMSG " + CHANNEL + " :Oh yeah. I am balls-deep in " + lastBall + "!";
                    break;
                case 2:
                    outmsg = "PRIVMSG " + CHANNEL + " :My mother always told me \"Go balls-deep in " + lastBall + "\".";
                    break;
                case 3:
                    outmsg = "PRIVMSG " + CHANNEL + " :Go balls-deep in " + lastBall + "!";
                    break;
                case 4:
                    outmsg = "PRIVMSG " + CHANNEL + " :I am currently balls-deep in " + lastBall + "!";
                    break;
                default:
                    outmsg = "PRIVMSG " + CHANNEL + " :I love it when I am balls-deep in " + lastBall + "!";
                    break;


            }
        } else  if (isBallRepMatch.Success) {
            String ballrepstr =isBallRepMatch.Value; 
            if (ballrepstr.Length >= 7) {
                // INcluded user name arg
                String repuser = ballrepstr.Substring(7).Trim();
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": " + repuser+" has a total reputation by votes on submitted 'balls-deep' items of " + getBallRep(repuser) + ".";

            } else {
                // Else default to current user
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Your total reputation by votes on submitted 'balls-deep' items is " + getBallRep(username) + ".";
            }
        } else if (isBallHistoryMatch.Success) {
            String voteHistory = "";
            String created="";
            foreach (KeyValuePair<String,Int16> kvp in ballsDeep[lastBall]) {
                if (kvp.Value==2) {
                    created= kvp.Key;
                } else {
                    if (kvp.Value==1) { voteHistory += kvp.Key+" +1; ";}
                    if (kvp.Value==-1) { voteHistory += kvp.Key+" -1; ";}
                }
            }

            outmsg = "PRIVMSG " + CHANNEL + " :Balls-deep in " + lastBall + ". History: Created by " + created + ". " + voteHistory; 
        } else if (isBallScoreMatch.Success) {
            if (lastBall.Equals("")) {
                outmsg = "PRIVMSG " + CHANNEL + " :I haven't been balls-deep in anything yet!";
            } else {
                outmsg = "PRIVMSG " + CHANNEL + " :Balls-deep in " + lastBall + ". Score = " + getBallScore(lastBall).First().Value + ", added by " + getBallScore(lastBall).First().Key+".";
            }
        } else if (isAddBallMatch.Success) {
            outmsg = "PRIVMSG " + CHANNEL + " :" + ballVote(input.Substring(4).Trim(), 2, username);
        } else if (isUpBallMatch.Success) {
            String tmpout = ballVote(lastBall, 1, username);
        if (tmpout.Length > 0) outmsg = "PRIVMSG " + CHANNEL + " :" + tmpout;
        } else if (isDownBallMatch.Success) {
            String tmpout = ballVote(lastBall, -1, username);
        if (tmpout.Length > 0) outmsg = "PRIVMSG " + CHANNEL + " :" + tmpout;
    } else if (stockMatch.Success) {
            String stockinfo = FetchStocks(stockMatch.Value.Substring(7));
            outmsg = "PRIVMSG " + CHANNEL + " :" + stockinfo;
        } else if (newsMatch.Success) {
            addToConsole("Getting Reddit news headline");
            String subreddit = "";
            if (input.Length > 6) { subreddit = input.Substring(6); }
            outmsg = "PRIVMSG " + CHANNEL + " :" + RedditNews(subreddit.Trim());
        } else if (weatherMatch.Success) {
// NEED TO USE A DIFFERENT API FOR WEATHER!
            String location = weatherMatch.Value.Substring(8);

            addToConsole("Getting weather info for " + location);
            outmsg = "PRIVMSG " + CHANNEL + " :" + Weather(location);
        } else if (botMatch.Success) {
            choice = random.Next(0, 4);
            switch (choice) {
                case 0:

                    outmsg = "PRIVMSG " + CHANNEL + " :I am not a bot!";
                    break;
                case 1:
                    List<String> activeBalls = getActiveBalls();
                    lastBall = activeBalls.ElementAt(random.Next(0, activeBalls.Count)).ToString();
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Fuck off! I am balls-deep in " + lastBall + " right now!";
                    break;
                case 2:
                    outmsg = "PRIVMSG " + CHANNEL + " :DONG!";
                    break;
                default:
                    outmsg = "PRIVMSG " + CHANNEL + " :" + username + ": Can you prove *you* aren't a bot?";
                    break;
            }
        } 
        return outmsg;
    }


    // This might be a good place to put some commands I could issue to the BOT. Maybe have some states that make it do something?
    static private String PrivResponses(String input, String username) {
        String outmsg = "";
        addToConsole("Private message was: " + input);
        if (username.Equals("dialman") || username.Equals("dialy") || username.Equals("dialmaster")) {
            Regex isSay = new Regex(@"^say", RegexOptions.IgnoreCase);
            Match isSayMatch = isSay.Match(input);

            if (isSayMatch.Success) {
                outmsg = "PRIVMSG " + CHANNEL + " :" + input.Substring(4);
            }
        } else {
            outmsg = "PRIVMSG " + username + " :Hey there " + username + ". You want a private chat?";
        }
        return outmsg;
    }

    // A random mix of greetings for people
    static private String Greeting(String username) {
        String outmsg = "";
        int choice = random.Next(0, 4);
        switch (choice) {
            case 0:
                outmsg = "PRIVMSG " + CHANNEL + " :Hi " + username +
                    ". Welcome to " + CHANNEL + ". I love you!";
                break;
            case 1:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username +
                    "! I thought I felt my balls tingling!";
                break;
            case 2:
                outmsg = "PRIVMSG " + CHANNEL + " :It's a bird, it's a DONG, it's " + username +
                    "!";
                break;
            case 3:
                outmsg = "PRIVMSG " + CHANNEL + " :" + username + ". Just the person I've been waiting for!";
                break;
            default:
                outmsg = "PRIVMSG " + CHANNEL + " :GreetDONGS " + username +
                    ".";
                break;
        }

        return outmsg;
    }

    // Going to use worldweatheronline now
    // API KEY is 01f0090cf4181436121809
    public static String Weather(string location) {
        String response = "";
        HttpWebRequest WeatherRequest;
        HttpWebResponse WeatherResponse = null;
        XmlDocument WeatherXMLDoc = null;



        try {
            // Example request: http://free.worldweatheronline.com/feed/weather.ashx?q=97322&format=xml&num_of_days=2&key=01f0090cf4181436121809
            WeatherRequest = (HttpWebRequest)WebRequest.Create("http://free.worldweatheronline.com/feed/weather.ashx?q=" + location + "&format=xml&num_of_days=2&key=01f0090cf4181436121809");
            WeatherResponse = (HttpWebResponse)WeatherRequest.GetResponse();
            WeatherXMLDoc = new XmlDocument();
            WeatherXMLDoc.Load(WeatherResponse.GetResponseStream());
            XmlNode root = WeatherXMLDoc.DocumentElement;
            XmlNodeList nodeList = root.SelectNodes("current_condition");
            // Exception is getting thrown here: Exception: Object reference not set to an instance of an object
            response = "The weather at zip code" + location + " is " + nodeList.Item(0).SelectSingleNode("temp_F").InnerText + " degrees and " + nodeList.Item(0).SelectSingleNode("weatherDesc").InnerText;
        } catch (System.Exception ex) {
            addToConsole("Exception " + ex.Message);
            return "I had a problem getting weather info for zip code" + location + ".";
        } finally {
            WeatherResponse.Close();
        }
        return response;
    }

    static private String RedditNews(String subreddit) {
        String response = "";
        HttpWebRequest RedditRequest;
        HttpWebResponse RedditResponse = null;
        XmlDocument RedditXMLdoc = null;
        if (subreddit.Equals("")) { subreddit = "all"; }

        try {

            int retries = 3;
            while (true) {
                try {
                    addToConsole("About to create webrequest...");
                    RedditRequest = (HttpWebRequest)WebRequest.Create("http://www.reddit.com/r/" + subreddit + ".rss");
                    RedditRequest.Timeout = 6000;
                    addToConsole("About to get response...");
                    RedditResponse = (HttpWebResponse)RedditRequest.GetResponse();
                    break;
                } catch (System.Exception ex) {
                    // If we got a 500 error than TRY AGAIN!
                    if (--retries == 0) {
                        throw;
                    } else {
                        Thread.Sleep(2000);
                        addToConsole("Exception " + ex.ToString() + ", try again...");
                    }
                }
            }
            addToConsole("About to create xml...");
            //+        ResponseUri    {http://www.reddit.com/reddits/search?q=poopypants}    System.Uri
            if (RedditResponse.ResponseUri.AbsolutePath.Equals("/reddits/search")) {
                return "I had a problem fetching the news.";
            }
            RedditXMLdoc = new XmlDocument();
            Stream responseStream = RedditResponse.GetResponseStream();
            RedditXMLdoc.Load(responseStream);
            XmlNode root = RedditXMLdoc.DocumentElement;
            XmlNodeList nodeList = root.SelectNodes("channel/item");
            // How many did we get?
            if (nodeList.Count < 1) { return "I had a problem fetching the news."; }
            int choice = random.Next(0, nodeList.Count - 1);

            XmlNode ourChoice = nodeList.Item(choice);
            XmlNode title = ourChoice.SelectSingleNode("title");


            response = title.InnerText + ": " + ourChoice.SelectSingleNode("link").InnerText;
        } catch (System.Exception ex) {
            addToConsole("Exception " + ex.Message);
            return "I had a problem fetching the news.";
        }
        return response;
    }



    static private String FetchStocks(string stockSymb) {
        String response = "";
        HttpWebRequest StockRequest;
        HttpWebResponse StockResponse = null;
        // used to build entire input
        StringBuilder sb = new StringBuilder();

        // used on each read operation
        byte[] buf = new byte[8192];
        addToConsole("Attempting to fetch data for " + stockSymb);
        try {
            StockRequest = (HttpWebRequest)WebRequest.Create("http://finance.yahoo.com/d/quotes.csv?s=" + stockSymb + "&f=l1");
            StockResponse = (HttpWebResponse)StockRequest.GetResponse();
            // we will read data via the response stream
            Stream resStream = StockResponse.GetResponseStream();

            string tempString = null;
            int count = 0;

            do {
                // fill the buffer with data
                count = resStream.Read(buf, 0, buf.Length);

                // make sure we read some data
                if (count != 0) {
                    // translate from bytes to ASCII text
                    tempString = Encoding.ASCII.GetString(buf, 0, count);

                    // continue building the string
                    sb.Append(tempString);
                }
            }
            while (count > 0); // any more data to read?

            // print out page source
            addToConsole("Stock data is " + sb.ToString());

        } catch (System.Exception ex) {
            addToConsole("Exception " + ex.Message);
            return "I had a problem fetching stock information for symbol '" + stockSymb + "'...";
        }

        if (sb.ToString().StartsWith("0.00")) {
            response = "I had a problem fetching stock information for symbol '" + stockSymb + "'...";
        } else {
            response = stockSymb + " last traded at " + sb.ToString();
        }

        return response;

    }

    // Initialize the store by filling it.
    private static void fillStore() {
        foreach (String ballmsg in ballsDeep.Keys) {
            Int16 totalvotes = getBallScore(ballmsg).First().Value;
            String user = getBallScore(ballmsg).First().Key;
            String score = totalvotes.ToString();
            bdstore.AppendValues (user,score,ballmsg,getVoteString(ballmsg));
        }    
    }


    // Update the store by either changing or adding a new ball message    
    private static void updateStore(String ballmsg) {
        // We just need to match the message here... If no match ADD. If there is a match, update the score
        Int16 totalvotes = getBallScore(ballmsg).First().Value;
        String user = getBallScore(ballmsg).First().Key;
        String score = totalvotes.ToString();
        TreeIter myiter;
        bool success = bdstore.GetIterFirst(out myiter);
        do {
            String ballstring = bdstore.GetValue(myiter,2).ToString();
            if (ballstring.Equals(ballmsg)) {
                addToConsole("Found that " + ballmsg+" already existed! Updating score in treestore to " + score);            
                bdstore.SetValue(myiter,1,score);
                bdstore.SetValue(myiter,3,getVoteString(ballmsg));                
                return;            
            }        
            success = bdstore.IterNext(ref myiter);
        } while (success);    
        addToConsole("Found that " + ballmsg+" was new!");            
        String voteString = " ";

        Console.WriteLine("Found that " + ballmsg+" was new! Score is " + score + ", user is "+user+". Votestring: " + voteString);
        
        bdstore.AppendValues (user, score,ballmsg,voteString);

        return;
    }

    private static void LoadBalls() {
        string[] file = File.ReadAllLines(@"ballsdeep.txt");

        foreach (String thisLine in file) {
            String[] splitLine = thisLine.Split('\t');
            String ballmsg = splitLine[0];
            Dictionary<String, Int16> innerDict = new Dictionary<String, Int16>();
            for (int i = 1; i < splitLine.Count()-1; i += 2) {
                innerDict[splitLine[i]] = Convert.ToInt16(splitLine[i + 1]);
            }
            ballsDeep[ballmsg] = innerDict;
        }
    }

    private static void SaveBalls() {
        // create a writer and open the file
        TextWriter tw = new StreamWriter(@"ballsdeep.txt");

        // write a line of text to the file
        foreach (KeyValuePair<String, Dictionary<String,Int16>> kvp in ballsDeep) {
            String thisLine = kvp.Key;
            foreach (KeyValuePair<String, Int16> innerkvp in kvp.Value) {
                thisLine = thisLine + '\t' + innerkvp.Key + '\t' + innerkvp.Value;
            }
            tw.WriteLine(thisLine);
        }

        // close the stream
        tw.Close();

    }


 }
