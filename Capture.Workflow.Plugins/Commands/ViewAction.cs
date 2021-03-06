﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Capture.Workflow.Core;
using Capture.Workflow.Core.Classes;
using Capture.Workflow.Core.Classes.Attributes;
using Capture.Workflow.Core.Interface;

namespace Capture.Workflow.Plugins.Commands
{
    [Description("")]
    [PluginType(PluginType.Command)]
    [DisplayName("ViewAction")]
    public class ViewAction : IWorkflowCommand
    {
        public string Name { get; set; }
        public WorkFlowCommand CreateCommand()
        {
            var command = new WorkFlowCommand();
            command.Properties.Add(new CustomProperty()
            {
                Name = "Action",
                PropertyType = CustomPropertyType.ValueList,
                ValueList = new List<string>() { "ShowView"}
            });

            command.Properties.Add(new CustomProperty()
            {
                Name = "ViewName",
                PropertyType = CustomPropertyType.View
            });
            return command;
        }

        public bool Execute(WorkFlowCommand command)
        {
            switch (command.Properties["Action"].Value)
            {
                case "ShowView":
                    WorkflowManager.Instance.OnMessage(new MessageEventArgs(Messages.ShowView,
                        command.Properties["ViewName"].Value));
                    break;
            }
            return true;
        }
    }
}
