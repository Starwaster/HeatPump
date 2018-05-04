//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace HeatPumps
{
	public class ModuleHeatPump: PartModule, IAnalyticPreview, IAnalyticTemperatureModifier
	{
		public class ResourceRate
		{
            protected string _name;
			protected string _unitName;
            protected FloatCurve _rate;

			public int id
			{
				get 
				{
					return _name.GetHashCode ();
				}
			}

            public string name
            {
                get
                {
                    return _name;
                }
            }

            public string unitName
            {
                get
                {
                    return _unitName;
                }
            }

            public FloatCurve rate
            {
                get
                {
                    return _rate;
                }
            }

            public ResourceRate(string name, FloatCurve rate, string unitName)
			{
				this._name = name;
				this._rate = rate;
				this._unitName = unitName;
			}
			
		}
		
		[KSPField(isPersistant = false, guiActive = true, guiName = "Efficiency", guiUnits = "",   guiFormat = "")]
		public string efficiencyDisplay = "100";

		[KSPField(isPersistant = false, guiActive = true, guiName = "Heat Transfer", guiUnits = "",   guiFormat = "")]
		private string heatTransferDisplay = "0 W";

		[KSPAction ("Activate Heat Pump")]
		public void ActivateAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			Activate ();
		}
		
		[KSPAction ("Shutdown Heat Pump")]
		public void ShutdownAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			Shutdown ();
		}
		
		[KSPAction ("Toggle Heat Pump")]
		public void ToggleAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			if (isActive)
				Shutdown ();
			else
				Activate ();
		}
		
		[KSPEvent(guiName = "Activate Heat Pump", guiActive = true)]
		public void Activate ()
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			isActive = true;
			Events ["Shutdown"].active = true;
			Events ["Activate"].active = false;
			//Events ["Shutdown"].guiActive = true;
			//Events ["Activate"].guiActive = false;
		}
		
		[KSPEvent(guiName = "Shutdown Heat Pump", guiActive = true)]
		public void Shutdown ()
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			isActive = false;
			Events ["Shutdown"].active = false;
			Events ["Activate"].active = true;
			//Events ["Shutdown"].guiActive = false;
			//Events ["Activate"].guiActive = true;
		}
        [KSPField(isPersistant = false)]
		public double radiatorMaxTempCap = 0.8;

		// TODO Deprecate this; would have performed function now performed by ModuleDeployableRadiator
		[KSPField]
		public bool useAnimationState = false;

		[KSPField]
		public bool useActionGroups = false;
		
		[KSPField(isPersistant = true)]
		public bool isActive = false;
		
		[KSPField(isPersistant = false)]
		public double heatTransfer = 10;

        [KSPField(isPersistant = false)]
		public double heatTransferCap = 100;

		[KSPField(isPersistant = false)]
		public double heatConductivity = 0.12;

		[KSPField(isPersistant = false)]
		public double skinInternalConductionMult = 0.001;
		
		public List<ResourceRate> inputResources;
        public List<ResourceRate> outputResources;

        public List<AttachNode> attachNodes = new List<AttachNode>();
		public List<string> attachNodeNames = new List<string>();

		private ModuleDeployableRadiator moduleDeployableRadiator;
		private double capTemp;
		private double skinCapTemp;
		private int radiatorCount;

        private double analyticInternalTemp;
        private double analyticSkinTemp;
        private double internalFluxAdjust;
        private double previousAnalyticTemp;
        private double PGConductionFactor;

        public FlightIntegrator flightIntegrator
        {
            get { return _flightIntegrator; }
        }

        public FlightIntegrator _flightIntegrator;

		public bool IsActive
		{
			get
			{
                if (HighLogic.LoadedSceneIsFlight)
                {
                    if ((object)moduleDeployableRadiator != null)
                        return moduleDeployableRadiator.deployState == ModuleDeployableRadiator.DeployState.EXTENDED;
                    else
                        return isActive;
                }
                return false;
			}
		}

		static string FormatFlux(double flux)
		{
			if (flux >= 1000000000.0)
				return (flux / 1000000000.0).ToString("F2") + " TW";
			else if (flux >= 1000000.0)
				return (flux / 1000000.0).ToString("F2") + " GW";
			else if (flux >= 1000.0)
				return (flux / 1000.0).ToString("F2") + " MW";
			else if (flux >= 1.0)
				return (flux).ToString("F2") + " kW";
			else
				return (flux * 1000.0).ToString("F2") + " W";
			
		}

		public override string GetInfo ()
		{
			string s;
			s = "Heat Pump: " + heatTransfer + " kW\n<color=#ff9900ff>- Requires up to:</color>\n";
			foreach (ResourceRate resource in inputResources)
			{
                double _rate = resource.rate.Evaluate(20.15f) * heatTransfer;
				if (_rate > 1)
					s += "  " + resource.name + ": " + _rate.ToString ("F2") + " " + resource.unitName + "/s\n";
				else if (_rate > 0.01666667f)
					s += "  " + resource.name + ": " + (_rate * 60).ToString ("F2") + " " + resource.unitName + "/m\n";
				else
					s += "  " + resource.name + ": " + (_rate * 3600).ToString ("F2") + " " + resource.unitName + "/h\n\n";
			}

            s += " Extra insulation resistance value: " + skinInternalConductionMult.ToString();

			return s;
		}
		
		public override void OnAwake ()
		{
			base.OnAwake ();
			inputResources = new List<ResourceRate> ();
            outputResources = new List<ResourceRate> ();
			attachNodes = new List<AttachNode>();
            heatTransferCap = Math.Max(heatTransferCap, heatTransfer); // should never be less than heatTransfer
		}
		
		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);
			foreach (ConfigNode n in node.GetNodes ("RESOURCE")) 
			{
                if (n.HasValue ("name") && n.HasNode ("rate")) 
				{
                    FloatCurve rate = new FloatCurve();
					string unitName = "";
					if (n.HasValue ("unitName"))
						unitName = n.GetValue ("unitName");
					else
						unitName = n.GetValue("name");
                    rate.Load(n.GetNode("rate"));

					inputResources.Add (new ResourceRate(n.GetValue("name"), rate, unitName));
					print ("adding RESOURCE " + n.GetValue("name") + " = " + rate.ToString());
				}
			}

            foreach (ConfigNode n in node.GetNodes ("OUTPUT_RESOURCE"))
            {
                if (n.HasValue ("name") && n.HasNode ("rate"))
                {
                    FloatCurve rate = new FloatCurve();
                    string unitName = "";
                    if (n.HasValue ("unitName"))
                        unitName = n.GetValue ("unitName");
                    else
                        unitName = n.GetValue ("name");
                    rate.Load(n.GetNode("rate"));

                    outputResources.Add (new ResourceRate (n.GetValue ("name"), rate, unitName));
                    print ("adding OUTPUT_RESOURCE " + n.GetValue ("name") + " = " + rate.ToString ());
                }
            }

            foreach (ConfigNode c in node.GetNodes("HEATPUMP_NODE"))
			{
				print("searching HEATPUMP_NODE");
				if (c.HasValue("name"))
				{
					string nodeName = c.GetValue("name");
					attachNodeNames.Add(nodeName);
                    print ("Adding HEATPUMP_NODE " + nodeName);
				}
				
			}
			
		}
		
		public override void OnStart (StartState state)
		{	
			base.OnStart (state);

			skinCapTemp = part.skinMaxTemp * radiatorMaxTempCap;
			capTemp = part.maxTemp * radiatorMaxTempCap;

			GameEvents.onVesselWasModified.Add (OnVesselWasModified);

			if ((object)moduleDeployableRadiator == null)
			{
				Events ["Shutdown"].active = isActive;
				Events ["Activate"].active = !isActive;
			}
			else
			{
				Events ["Shutdown"].active = false;
				Events ["Activate"].active = false;
				Actions["ToggleAction"].active = false;
				Actions["ActivateAction"].active = false;
				Actions["ShutdownAction"].active = false;
			}
			if (inputResources.Count == 0 && part.partInfo != null) 
			{
				inputResources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).inputResources;
			}
            if (outputResources.Count == 0 && part.partInfo != null) {
                outputResources = ((ModuleHeatPump)part.partInfo.partPrefab.Modules ["ModuleHeatPump"]).outputResources;
            }
            if (attachNodes.Count == 0)
			{
				foreach (string nodeName in attachNodeNames)
				{
					AttachNode node = part.FindAttachNode (nodeName);
					if ((object)node != null)
					{
						print ("Found AttachNode: " + nodeName);
						attachNodes.Add(node);
						if ((object)node.attachedPart != null)
						{
							print ("Found attached part: " + node.attachedPart.name);
							node.attachedPart.heatConductivity = Math.Min (heatConductivity, node.attachedPart.heatConductivity);
							node.attachedPart.skinInternalConductionMult = Math.Min (skinInternalConductionMult, node.attachedPart.skinInternalConductionMult);
						}
					}
				}
			}
			if ((object)part.srfAttachNode.attachedPart != null)
			{
				part.srfAttachNode.attachedPart.heatConductivity = Math.Min (heatConductivity, part.srfAttachNode.attachedPart.heatConductivity);
				part.srfAttachNode.attachedPart.skinInternalConductionMult = Math.Min (skinInternalConductionMult, part.srfAttachNode.attachedPart.skinInternalConductionMult);	
			}

            if (HighLogic.LoadedSceneIsFlight)
            {
                for (int i = 0; i < vessel.vesselModules.Count; i++)
                {
                    if (vessel.vesselModules[i] is FlightIntegrator)
                    {
                        _flightIntegrator = vessel.vesselModules[i] as FlightIntegrator;
                    }
                }

                moduleDeployableRadiator = part.FindModuleImplementing<ModuleDeployableRadiator>();
                radiatorCount = part.symmetryCounterparts.Count + 1;
            }
        }

		public void OnVesselWasModified(Vessel v)
		{
			//if (v == part.vessel)
			//	radiatorCount = part.symmetryCounterparts.Count + 1;
		}
		
		void FixedUpdate()
		{
            if (!IsActive)
			{
				efficiencyDisplay = "0%";
				heatTransferDisplay = "0 W";
				return;
			}

            PGConductionFactor = Math.Max(1.0d, PhysicsGlobals.ConductionFactor);

            radiatorCount = 1;
            foreach (Part p in part.symmetryCounterparts)
            {
                ModuleHeatPump hp = p.Modules["ModuleHeatPump"] as ModuleHeatPump;
                if (hp.IsActive)
                    radiatorCount += 1;
            }

            if (!flightIntegrator.isAnalytical)
            {
                foreach (AttachNode attachNode in attachNodes)
                {
                    Part targetPart = attachNode.attachedPart;

                    if ((object)targetPart == null)
                        continue;

                    ProcessCooling(targetPart);
                }
                if (part.srfAttachNode.attachedPart != null)
				ProcessCooling(part.srfAttachNode.attachedPart);
            }
		}
		
        public void ProcessCooling(Part targetPart, bool analyticalMode = false)
		{
            if (!IsActive)
                return;
			double requested = 0d;
			double efficiency = 1d;
			double _heatTransfer = 0d;
			double conductionCompensation = 0d;

            double tempDelta = 0d;
            double targetTemp = targetPart.maxTemp * targetPart.radiatorMax;

            /*
            if (!analyticalMode)
                tempDelta = targetPart.temperature - targetTemp;
            else
            {
                analyticalTemp = (previousAnalyticTemp + analyticInternalTemp)/2;
                tempDelta = Math.Max(part.temperature, analyticalTemp) - targetTemp;
                previousAnalyticTemp = analyticInternalTemp;
            }
            */

            tempDelta = targetPart.temperature - targetTemp;

            if (tempDelta > 0d)
            {
                // Never drop below the heatTransfer value; fractional values makes it harder to reach our temperature goals
                _heatTransfer = Math.Max(heatTransfer * tempDelta, heatTransfer);
                _heatTransfer = Math.Min(_heatTransfer, heatTransferCap);
            }

            // Throttle back if part is getting too hot.
            if (Math.Max(part.temperature, analyticInternalTemp) >= capTemp || Math.Max(part.skinTemperature, analyticSkinTemp) >= skinCapTemp)
                //conductionCompensation *= Math.Min (capTemp / part.temperature, skinCapTemp / part.skinTemperature);
                return;
            if (!analyticalMode)
            {
                if (targetPart.thermalConductionFlux > 0.0)
                    conductionCompensation += (targetPart.thermalConductionFlux);
                if (targetPart.skinToInternalFlux > 0.0)
                    conductionCompensation += (targetPart.skinToInternalFlux);
            }
            else
            {
                // Analytical mode means we don't have conduction information available. Improvise.
                //conductionCompensation = Math.Pow(tempDelta, 4d);
                //conductionCompensation = Math.Max(tempDelta, 0) * targetPart.thermalMass;
            }

			// Only counting radiators placed symmetrically for this to ensure that only heat pumps targeting the same parts are counted.
			conductionCompensation /= radiatorCount;

            // Temporarily ceasing resource consumption during analytical mode pending evaluation of cooling rate.
            // TODO: Investigate reinstatement of resource consumption during analytical mode.
            if (!analyticalMode)
            {
                foreach (ResourceRate resource in inputResources)
                {
                    double availableResources = 0;
                    double rate = resource.rate.Evaluate((float)targetPart.temperature);
                    if (rate > 0 && targetPart.temperature < part.temperature)
                    {
                        // Divided by PG.ConductionFactor because it gets WAY too expensive compensating for inflated conduction factors.
                        requested = (rate * _heatTransfer) + (rate * conductionCompensation);

                        requested *= TimeWarp.fixedDeltaTime;
                        availableResources = part.RequestResource(resource.id, requested);
                        if (efficiency > availableResources / requested)
						efficiency = availableResources / requested;
                    }
                }
            }

            float cost = inputResources[0].rate.Evaluate((float)targetPart.temperature);
            efficiencyDisplay = (efficiency).ToString ("P") + "(cost = " + cost.ToString("F2") + ")";
            heatTransferDisplay = FormatFlux((_heatTransfer + conductionCompensation) * efficiency) + "x" + radiatorCount.ToString();
			
            targetPart.AddThermalFlux(-(_heatTransfer + conductionCompensation) * efficiency);

            part.AddSkinThermalFlux ((_heatTransfer + conductionCompensation) * efficiency);
      
            foreach (ResourceRate resource in outputResources)
            {
                double rate = resource.rate.Evaluate((float)targetPart.temperature);
                if (rate > 0 &&  targetPart.temperature < part.temperature)
                {
                    requested = (rate * _heatTransfer);
                    requested *= TimeWarp.fixedDeltaTime;
                    part.RequestResource (resource.id, -requested);
                }
            }
        }

		static void print(string msg)
		{
			MonoBehaviour.print("[HeatPump] " + msg);
		}

        #region Analytic Interfaces
        // Analytic Interface
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            analyticSkinTemp = toBeSkin;
            analyticInternalTemp = toBeInternal;
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = true;
            return analyticSkinTemp;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = false;
            return analyticInternalTemp;
        }

        // Analytic Preview Interface
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (_flightIntegrator != fi)
                _flightIntegrator = fi;

            //analyticalInternalFlux = internalFlux;
            //float deltaTime = (float)(Planetarium.GetUniversalTime() - vessel.lastUT);
            if (IsActive)
            {
                //Debug.Log("[ModuleHeatPump] UT = " + Planetarium.GetUniversalTime().ToString() + ", internalFlux = " + internalFlux.ToString());
                foreach (AttachNode attachNode in attachNodes)
                {
                    Part targetPart = attachNode.attachedPart;

                    if ((object)targetPart == null)
                        continue;

                    ProcessCooling(targetPart, true);
                }
                if (part.srfAttachNode.attachedPart != null)
                    ProcessCooling(part.srfAttachNode.attachedPart, true);
            }
        }

        public double InternalFluxAdjust()
        {
            return internalFluxAdjust;
        }

        #endregion
    }
}