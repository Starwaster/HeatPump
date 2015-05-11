//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
	public class ModuleHeatPump: PartModule
	{
		public class ResourceRate
		{
			public string name;
			public float rate;
			public int id
			{
				get 
				{
					return name.GetHashCode ();
				}
			}
			public ResourceRate(string name, float rate)
			{
				this.name = name;
				this.rate = rate;
			}
			
		}
		
		[KSPAction ("Activate Heat Pump")]
		public void ActivateAction (KSPActionParam param)
		{
			Activate ();
		}
		
		[KSPAction ("Shutdown Heat Pump")]
		public void ShutdownAction (KSPActionParam param)
		{
			Shutdown ();
		}
		
		[KSPAction ("Toggle Heat Pump")]
		public void ToggleAction (KSPActionParam param)
		{
			if(isActive)
				Shutdown ();
			else
				Activate ();
		}
		
		[KSPEvent(guiName = "Activate Heat Pump", guiActive = true)]
		public void Activate ()
		{
			isActive = true;
			Events ["Shutdown"].active = true;
			Events ["Activate"].active = false;
			//Events ["Shutdown"].guiActive = true;
			//Events ["Activate"].guiActive = false;
		}
		
		[KSPEvent(guiName = "Shutdown Heat Pump", guiActive = true)]
		public void Shutdown ()
		{
			isActive = false;
			Events ["Shutdown"].active = false;
			Events ["Activate"].active = true;
			//Events ["Shutdown"].guiActive = false;
			//Events ["Activate"].guiActive = true;
		}
		[KSPField]
		public bool useAnimationState = false;

		[KSPField]
		public bool useActionGroups = false;
		
		[KSPField(isPersistant = true)]
		public bool isActive = false;
		
		[KSPField(isPersistant = false)]
		public float heatTransfer = 1.0f;
		
		[KSPField(isPersistant = false)]
		public float heatGain = 0.0f;
		
		public List<ResourceRate> resources;
		
		public List<AttachNode> attachNodes = new List<AttachNode>();
		public List<string> attachNodeNames = new List<string>(); 
		
		public override string GetInfo ()
		{
			string s;
			s = "Heat Pump: " + heatTransfer + "/s\nRequirements:\n";
			foreach (ResourceRate resource in resources)
			{
				if(resource.rate > 1)
					s += "  " + resource.name + ": " + resource.rate.ToString ("F2") + "/s\n";
				else if(resource.rate > 0.01666667f)
					s += "  " + resource.name + ": " + (resource.rate * 60).ToString ("F2") + "/m\n";
				else
					s += "  " + resource.name + ": " + (resource.rate * 3600).ToString ("F2") + "/h\n";
			}
			
			return s;
		}
		
		public override void OnAwake ()
		{
			base.OnAwake ();
			resources = new List<ResourceRate> ();
			attachNodes = new List<AttachNode>();
		}
		
		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);
			foreach (ConfigNode n in node.GetNodes ("RESOURCE")) 
			{
				if(n.HasValue ("name") && n.HasValue ("rate")) 
				{
					float rate;
					float.TryParse (n.GetValue ("rate"), out rate);
					resources.Add (new ResourceRate(n.GetValue("name"), rate));
				}
			}
			foreach (ConfigNode c in node.GetNodes("HEATPUMP_NODE"))
			{
				// It would be easier to do this by just reading multiple names from one node
				// Doing it this way allows for expansion later such as other attributes in each HEATPUMP_NODE
				print("*RF* Heatpump searching HEATPUMP_NODE");
				if (c.HasValue("name"))
				{
					string nodeName = c.GetValue("name");
					print("*RF* Heatpump adding " + nodeName);
					attachNodeNames.Add(nodeName);
				}
				
			}
			
		}
		
		public override void OnStart (StartState state)
		{
			base.OnStart (state);

			Events ["Shutdown"].active = false;
			Events ["Activate"].active = true;
			
			if(resources.Count == 0 && part.partInfo != null) 
			{
				if(part.partInfo.partPrefab.Modules.Contains ("ModuleHeatPump"))
					resources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).resources;
			}
			if (attachNodes.Count == 0)
			{
				foreach (string nodeName in attachNodeNames)
				{
					AttachNode node = this.part.findAttachNode(nodeName);
					if ((object)node != null)
						attachNodes.Add(node);
				}
			}
		}
		
		void FixedUpdate()
		{
			if (!HighLogic.LoadedSceneIsFlight || !isActive)
			{
				return;
			}
			
			foreach (AttachNode attachNode in attachNodes)
			{
				Part targetPart = attachNode.attachedPart;
				
				if ((object)targetPart == null)
					continue;
				
				ProcessCooling(targetPart);
			}
			ProcessCooling(this.part.parent);
		}
		
		public void ProcessCooling(Part targetPart)
		{
			double efficiency = (targetPart.temperature + 546.15) / (targetPart.temperature + 573.15);
			if (targetPart.temperature < 0)
				efficiency = 0;
			if (heatTransfer < 0) 
			{
				efficiency = (part.temperature + 546.15) / (part.temperature + 573.15);
				if(part.temperature < 0)
					efficiency = 0;
			}
			foreach (ResourceRate resource in resources)
			{
				if(resource.rate > 0)
				{
					float available = part.RequestResource(resource.id, resource.rate);
					if(efficiency > available / resource.rate)
						efficiency = available / resource.rate;
				}
			}
			// Uses KSP 1.0 InternalHeatFlux now
			targetPart.AddThermalFlux(efficiency * heatTransfer * Time.fixedDeltaTime);
			part.AddThermalFlux (efficiency * heatTransfer * Time.fixedDeltaTime);
		}
	}
}