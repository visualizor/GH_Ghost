using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace GH_Ghost
{
    public class GH_GhostInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "GH_Ghost";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return Properties.Resources.GhostWorker;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "a single component that works other components on separate threads";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("2a09f1b8-fbcb-4a32-b175-d17f86eb7299");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "Will Wang";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return @"will.wang6@gmail.com";
            }
        }
    }
}
