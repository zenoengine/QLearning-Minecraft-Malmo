// --------------------------------------------------------------------------------------------------
//  Copyright (c) 2016 Microsoft Corporation
//  
//  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
//  associated documentation files (the "Software"), to deal in the Software without restriction,
//  including without limitation the rights to use, copy, modify, merge, publish, distribute,
//  sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//  
//  The above copyright notice and this permission notice shall be included in all copies or
//  substantial portions of the Software.
//  
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
//  NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
//  DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// --------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Research.Malmo;
using System.Xml;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ActionTable : Dictionary<string, double>
{
    public ActionTable(string[] actions)
    {
        foreach (var action in actions)
        {
            Add(action, 0);
        }
    }
}

public class QTable : Dictionary<string, ActionTable> { }

class QAgent
{

    string[] mActions = { "movenorth 1", "movesouth 1", "movewest 1", "moveeast 1" };

    string mCurrentState;
    string mPrevState;
    string mPrevAction;

    float alpha = 0.1f;
    float gamma = 1.0f;
    float epsilon = 0.1f;


    QTable mQTable = new QTable();

    public QAgent()
    {
    }

    double GetActionTableMaxQ(ActionTable table)
    {
        if (table.Count == 0)
        {
            return 0;
        }

        double max = float.MinValue;
        foreach (var element in table)
        {
            if (max < element.Value)
            {
                max = element.Value;
            }
        }

        return max;
    }

    void UpdateQTable(double reward, string currentState)
    {
        double oldQ = mQTable[mPrevState][mPrevAction];
        double newQ = oldQ + alpha * (reward + gamma * GetActionTableMaxQ(mQTable[currentState]) - mQTable[mPrevState][mPrevAction]);
        mQTable[mPrevState][mPrevAction] = newQ;
    }

    void UpdateQTableFromTerminatingState(double reward)
    {
        double oldQ = mQTable[mPrevState][mPrevAction];
        double newQ = oldQ + alpha * (reward);
        mQTable[mCurrentState][mPrevAction] = newQ;
    }
    
    class PosData
    {
        private string xPos = "";
        private string yPos = "";
        private string zPos = "";

        public string XPos { get => xPos; set => xPos = value; }
        public string YPos { get => yPos; set => yPos = value; }
        public string ZPos { get => zPos; set => zPos = value; }
    }

    public void Act(WorldState worldState, AgentHost agentHost, double reward)
    {
        TimestampedString observationInfo = worldState.observations[worldState.observations.Count - 1];
        PosData posData = JsonConvert.DeserializeObject<PosData>(observationInfo.text);

        mCurrentState = posData.XPos + ":" + posData.ZPos;
        
        if (!mQTable.ContainsKey(mCurrentState))
        {
            ActionTable actionTable = new ActionTable(mActions);
            mQTable.Add(mCurrentState, actionTable);
        }

        if (mPrevState != null && mPrevAction != null)
        {
            UpdateQTable(reward, mCurrentState);
        }

        string action = "";

        //If action decision smaller than epsilon, do random action for exploration.
        Random random = new Random();
        double actionDecisionValue = random.NextDouble();
        if (actionDecisionValue < epsilon)
        {
            int randomActionIndex = random.Next() % mActions.Length;
            action = mActions[randomActionIndex];
        }
        else
        {
            ActionTable currentActionTable = mQTable[mCurrentState];

            double max = double.MinValue;
            foreach (var element in currentActionTable)
            {
                if (max < element.Value)
                {
                    action = element.Key;
                    max = element.Value;
                }
            }
        }

        agentHost.sendCommand(action);
        mPrevState = mCurrentState;
        mPrevAction = action;
    }

    public void Run(AgentHost agentHost)
    {
        bool isFirstAction = true;
        mPrevState = null;
        mPrevAction = null;

        WorldState worldState = agentHost.getWorldState();
        double currentReward = 0;
        while (worldState.is_mission_running)
        {
            Thread.Sleep(10);
            
            currentReward = 0;
            if (isFirstAction)
            {
                while (true)
                {
                    worldState = agentHost.getWorldState();

                    foreach (var error in worldState.errors)
                    {
                        Console.Write("error : " + error.text);
                    }

                    foreach (var reward in worldState.rewards)
                    {
                        currentReward += reward.getValue();
                    }

                    if (worldState.observations.Count > 0 &&
                    worldState.observations[worldState.observations.Count - 1].text != "{}")
                    {
                        if (worldState.rewards.Count > 0)
                        {
                            foreach (var rewardData in worldState.rewards)
                            {
                                currentReward += rewardData.getValue();
                            }
                        }

                        Act(worldState, agentHost, currentReward);
                        isFirstAction = false;
                        break;
                    }

                    if (!worldState.is_mission_running)
                    {
                        break;
                    }
                }

            }
            else
            {
                while (worldState.is_mission_running && currentReward == 0)
                {
                    Thread.Sleep(100);
                    worldState = agentHost.getWorldState();

                    foreach (var error in worldState.errors)
                    {
                        Console.Write("error : " + error.text);
                    }

                    foreach (var reward in worldState.rewards)
                    {
                        currentReward += reward.getValue();
                    }
                }

                while(true)
                {
                    Thread.Sleep(100);
                    worldState = agentHost.getWorldState();

                    foreach (var error in worldState.errors)
                    {
                        Console.Write("error : " + error.text);
                    }

                    foreach (var reward in worldState.rewards)
                    {
                        currentReward += reward.getValue();
                    }

                    if (worldState.observations.Count > 0 &&
                        worldState.observations[worldState.observations.Count - 1].text != "{}")
                    {
                        Act(worldState, agentHost, currentReward);
                    }
                    
                    if(!worldState.is_mission_running)
                    {
                        break;
                    }
                }
            }
        }

        if (mPrevAction != null && mPrevState != null)
        {
            Console.WriteLine("Die Reward : " + currentReward);
            UpdateQTableFromTerminatingState(currentReward);
        }
    }
}

class Program
{
    public static void Main()
    {
        QAgent agent = new QAgent();

        AgentHost agentHost = new AgentHost();
        try
        {
            agentHost.parse(new StringVector(Environment.GetCommandLineArgs()));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: {0}", ex.Message);
            Console.Error.WriteLine(agentHost.getUsage());
            Environment.Exit(1);
        }
        if (agentHost.receivedArgument("help"))
        {
            Console.Error.WriteLine(agentHost.getUsage());
            Environment.Exit(0);
        }

        XmlDocument doc = new XmlDocument();
        doc.Load("q_learning_problem.xml");
        MissionSpec mission = new MissionSpec(doc.OuterXml, true);
        mission.requestVideo(320, 240);
        mission.setViewpoint(1);

        MissionRecordSpec missionRecord = new MissionRecordSpec("./saved_data.tgz");
        missionRecord.recordCommands();
        missionRecord.recordMP4(20, 400000);
        missionRecord.recordRewards();
        missionRecord.recordObservations();

        const int MAX_TRIAL_NUM= 500;
        for (int trial = 0; trial < MAX_TRIAL_NUM; trial++)
        {
            Console.WriteLine(" ");
            Console.WriteLine("-------------------------");
            Console.WriteLine("Tiral Number : " + trial);
            Console.WriteLine("-------------------------");
            Console.WriteLine(" ");
            try
            {
                agentHost.startMission(mission, missionRecord);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error starting mission: {0}", ex.Message);
                Environment.Exit(1);
            }

            WorldState worldState;

            Console.WriteLine("Waiting for the mission to start");
            do
            {
                Console.Write(".");
                Thread.Sleep(100);
                worldState = agentHost.getWorldState();

                foreach (TimestampedString error in worldState.errors) Console.Error.WriteLine("Error: {0}", error.text);
            }
            while (!worldState.has_mission_begun);

            // main loop:
            agent.Run(agentHost);
            Thread.Sleep(10);
        }
     
        Console.WriteLine("Mission has stopped.");
    }
}
