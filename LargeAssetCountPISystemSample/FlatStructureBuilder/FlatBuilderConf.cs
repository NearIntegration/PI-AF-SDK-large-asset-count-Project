using System;
using System.Configuration;

namespace FlatStructureBuilder
{
    public class FlatBuilderConf
    {
        // AF configuration
        public string AFServerName { get; set; }
        public string AFDatabaseName { get; set; }
        public string PIServerName { get; set; }

        // Tree configuration
        public int TotalSubTrees { get; set; }
        public int BranchesPerSubTree { get; set; }
        public int LeavesPerBranch { get; set; }
        public int TotalLeafs { get; set; }
        public int LeavesPerSubTree { get; set; }

        // Job configuration
        public int ElementsPerCheckin { get; set; }
        public int MaxParallel { get; set; }
        public int TotalPoints { get; set; }
        public int PointsCreated { get; set; }
        public int PointsChunkSize { get; set; }
        public int ThresholdValue { get; set; }

        public bool Initialize()
        {
            this.AFServerName = ConfigurationManager.AppSettings["AF Server"];
            if (String.IsNullOrWhiteSpace(AFServerName))
            {
                Console.WriteLine("AF Server is missing in app.config.");
                return false;
            }

            this.AFDatabaseName = ConfigurationManager.AppSettings["AF Database"];
            if (String.IsNullOrWhiteSpace(AFDatabaseName))
            {
                Console.WriteLine("AF Database is missing in app.config.");
                return false;
            }

            this.PIServerName = ConfigurationManager.AppSettings["PI Data Archive"];
            if (String.IsNullOrWhiteSpace(PIServerName))
            {
                Console.WriteLine("PI Data Archive is missing in app.config.");
                return false;
            }

            int totalSubTrees;
            if (Int32.TryParse(ConfigurationManager.AppSettings["Total SubTrees"], out totalSubTrees))
            {
                this.TotalSubTrees = totalSubTrees;
            }
            else
            { 
                Console.WriteLine(@"""Total SubTrees"" invalid in app.config.");
                return false;
            }

            int branchesPerSubTree;
            if (Int32.TryParse(ConfigurationManager.AppSettings["Branches per SubTree"], out branchesPerSubTree))
            {
                this.BranchesPerSubTree = branchesPerSubTree;
            }
            else
            {
                Console.WriteLine(@"""Branches per SubTree"" invalid in app.config.");
                return false;
            }

            int leavesPerBranch;
            if (Int32.TryParse(ConfigurationManager.AppSettings["Leaves per Branch"], out leavesPerBranch))
            {
                this.LeavesPerBranch = leavesPerBranch;
            }
            else
            {
                Console.WriteLine(@"""Leaves per Branch"" invalid in app.config.");
                return false;
            }

            this.TotalLeafs = this.TotalSubTrees * this.BranchesPerSubTree * this.LeavesPerBranch;
            this.LeavesPerSubTree = this.TotalLeafs / this.TotalSubTrees;

            int numPerCheckin;
            if (Int32.TryParse(ConfigurationManager.AppSettings["Elements per CheckIn"], out numPerCheckin))
            {
                this.ElementsPerCheckin = numPerCheckin;
            }
            else
            {
                Console.WriteLine(@"""Elements per CheckIn"" invalid in app.config.");
                return false;
            }

            this.PointsCreated = 0;
            this.TotalPoints = this.TotalLeafs * 2;
            this.PointsChunkSize = 1000;
            this.ThresholdValue = 1000;

            int maxParallel;
            if (Int32.TryParse(ConfigurationManager.AppSettings["Max degrees of parallelism"], out maxParallel))
            {
                this.MaxParallel = maxParallel;
            }
            else
            {
                Console.WriteLine(@"""Max degrees of parallelism"" invalid in app.config.");
                return false;
            }

            bool wantToContinue = CheckDisplayWarning();

            if (wantToContinue)
            {
                Console.WriteLine("Total SubTrees: {0}", this.TotalSubTrees);
                Console.WriteLine("Branches per SubTree: {0}", this.BranchesPerSubTree);
                Console.WriteLine("Leaves per Branch: {0}", this.LeavesPerBranch);
                Console.WriteLine("Elements per CheckIn: {0}\n", this.ElementsPerCheckin);

                Console.WriteLine("Max degrees of parallelism: {0}\n", this.MaxParallel);
                return true;
            }
            else
            {
                return false;
            } 
        }

        private bool CheckDisplayWarning()
        {
            if (this.TotalLeafs >= 500)
            {
                Console.Write(string.Format(@"WARNING: This program will create {0} leaf elements and {1} PI Points. " +
                                            @"These PI Points will have PointSource = R, which by default is used by the PI Random Interface. " + 
                                            @"Press Y to continue, N to quit the program : ", 
                                             this.TotalLeafs, this.TotalPoints));
                while (true)
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine("\n");
                    if ((result.KeyChar == 'Y') || (result.KeyChar == 'y'))
                    {
                        return true;
                    }
                    else if ((result.KeyChar == 'N') || (result.KeyChar == 'n'))
                    {
                        return false;
                    }

                    Console.Write("Invalid input, try again (Y/N) : ");
                }
            }
            else
            {
                return true;
            }
        }
    }
}
