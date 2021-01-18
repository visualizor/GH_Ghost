using System;

using Grasshopper.Kernel;

namespace GH_Ghost
{
    public class GhostBuster : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GhostBuster class.
        /// </summary>
        public GhostBuster()
          : base("GhostBuster", "GhB",
              "query how many ghost workers are working",
              "Maths", "Script")
        {
        }

        protected void ExpireCallback(GH_Document doc)
        {
            ExpireSolution(false);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Automatic Refresh", "R", "set to true for this to check worker count periodically", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Intervals", "P", "intervals for periodic checks in milliseconds", GH_ParamAccess.item, 1000);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Worker Count", "N", "number of ghost workers on separate threads\nif this number is greater than the number of computing Ghosts on canvas\nit means there is a straggler process that can only be disposed by rhino shutdown", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool refresh = false;
            int itvl = 1000;
            DA.GetData(0, ref refresh);
            DA.GetData(1, ref itvl);
            if (itvl <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " time interval not realistic");
                return;
            }

            if (refresh)
            {
                OnPingDocument().ScheduleSolution(itvl, ExpireCallback);
            }
            
            DA.SetData(0, Ghost.WorkerCount);
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.Buster2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("136ffe80-d302-4b20-b73c-bff0f8dc6d3e"); }
        }
    }
}