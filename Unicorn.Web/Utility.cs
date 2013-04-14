﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Reflection;
using System.Xml;
using System.Web.Security;
using Unicorn.Web.Security.Authorization;
using System.Web;
using System.Linq;

namespace Unicorn.Web
{
    public static class Utility
    {
        public static void AddStyleSheet(Page p, string resourceKey)
        {
            if (p.Header == null)
                return;
            HtmlGenericControl li = new HtmlGenericControl("link");
            li.Attributes["rel"] = "stylesheet";
            li.Attributes["type"] = "text/css";
            li.Attributes["href"] = p.ClientScript.GetWebResourceUrl(p.GetType(), resourceKey);
            p.Header.Controls.Add(li);
        }

        internal static string[] SplitString(string original, char separator)
        {
            if (string.IsNullOrEmpty(original))
            {
                return new string[0];
            }
            List<string> split = new List<string>(original.Split(new char[] { separator }, StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < split.Count; i++)
            {
                split[i] = split[i].Trim();
                if (split[i] == string.Empty)
                    split.RemoveAt(i--);
            }
            return split.ToArray();
        }

        public static void BuildSiteMapMenu(object menu)
        {
            Type type = menu.GetType();
            PropertyInfo itemsProperty = type.GetProperty("Items", BindingFlags.Instance | BindingFlags.Public);
            object items = itemsProperty.GetValue(menu, null);
            MethodInfo[] methods = itemsProperty.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo clearMethod = methods.First(m => m.Name == "Clear");
            clearMethod.Invoke(items, null);
            MethodInfo addMethod = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            if (addMethod == null)
                addMethod = methods.First(m => m.Name == "Add" && !m.GetParameters()[0].GetType().FullName.EndsWith("Control"));
            XmlDocument doc = new XmlDocument();
            //Page p = (Page)type.GetProperty("Page").GetValue(menu, null);
            doc.Load(HttpContext.Current.Server.MapPath("~/Web.sitemap"));
            //doc.Load(HttpContext.Current.Server.MapPath("~/Web.sitemap"));
            Type itemType = addMethod.GetParameters()[0].ParameterType;
            BuildSiteMapMenu(menu, items, addMethod, itemType, doc.DocumentElement.FirstChild.ChildNodes, "Menu", Roles.GetRolesForUser(), false);
        }
        private static void BuildSiteMapMenu(object menu, object items, MethodInfo addMethod, Type itemType, XmlNodeList nodes, string parentAction, string[] roles, bool parentAccess)
        {
            foreach (XmlNode node in nodes)
            {
                if (node.NodeType == XmlNodeType.Comment)
                    continue;
                string action = GetNodeAttValue(node, "action");
                bool shouldReturn = false;
                //if user has access to parent node then he has access to this node and all child nodes. no need to check
                // more and 'action' is not needed.
                bool hasAccess = HasSiteMapNodeAccess(parentAction, roles, node, ref action, out shouldReturn);
                if (shouldReturn)
                    return;
                object menuItem = CreateMenuIem(node, itemType);
                PropertyInfo childItemsProp = itemType.GetProperty("Items");
                if (childItemsProp == null)
                {
                    childItemsProp = itemType.GetProperty("ChildItems");
                    if (childItemsProp == null)
                        childItemsProp = itemType.GetProperty("MenuItems");
                }
                object childItems = childItemsProp.GetValue(menuItem, null);
                var childAddMethod = childItemsProp.PropertyType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                BuildSiteMapMenu(menu, childItems, childAddMethod, itemType, node.ChildNodes, action, roles, hasAccess);
                if (hasAccess || (int)childItemsProp.PropertyType.GetProperty("Count").GetValue(childItems, null) > 0)
                {
                    if (addMethod.DeclaringType == items.GetType())
                        addMethod.Invoke(items, new object[] { menuItem });
                    else
                        addMethod.Invoke(menu, new object[] { menuItem });
                }
            }
        }

        private static string GetNodeAttValue(XmlNode node, string attributeName)
        {
            if (node.Attributes[attributeName] == null)
                return null;
            return node.Attributes[attributeName].Value;
        }

        public static bool HasSiteMapNodeAccess(string parentAction, string[] userRoles,
            XmlNode node, ref string action, out bool shouldReturn)
        {
            shouldReturn = false;
            //if (AuthorizationManager.GetAllActionsForUser("admin").Length == 0)
            //    return user.Identity.IsAuthenticated;
            if (node.NodeType == XmlNodeType.Comment)
                return true;
            XmlAttribute rolesAttrib = node.Attributes["roles"];
            XmlAttribute usersAttrib = node.Attributes["users"];
            XmlAttribute actionsAttrib = node.Attributes["actions"];
            XmlAttribute urlAttrib = node.Attributes["url"];
            XmlAttribute visibleAttrib = node.Attributes["visible"];
            if (visibleAttrib != null && visibleAttrib.Value.ToLower() == "false")
                return false;

            bool hasAccess = false;
            System.Security.Principal.IPrincipal user = HttpContext.Current.User;
            if (usersAttrib == null && rolesAttrib == null && actionsAttrib == null && action == null)
            {
                if (urlAttrib != null)
                {
                    action = parentAction + "." + urlAttrib.Value;
                    if (AuthorizationChecker.HasAccess(action))
                        hasAccess = true;
                }
            }
            else if (!user.Identity.IsAuthenticated)
            {
                shouldReturn = true;
                return false;
            }
            if (rolesAttrib != null)
            {
                string[] menuRoles = Utility.SplitString(rolesAttrib.Value, ',');
                if (menuRoles.Length == 0 || HasAnyOf(userRoles, menuRoles))
                    hasAccess = true;
                //else if (usersAttrib == null)
                //{
                //    //shouldReturn = true;
                //    return false;
                //}
            }

            string userName = user.Identity.Name.ToLower();
            if (usersAttrib != null)
            {
                string[] users = Utility.SplitString(usersAttrib.Value, ',');
                foreach (string u in users)
                {
                    if (userName == u.ToLower())
                        hasAccess = true;
                }
                //if (userName.Length > 0 && !hasAccess)
                //{
                //    //shouldReturn = true;
                //    return false;
                //}
            }
            if (actionsAttrib != null)
            {
                AuthorizeActionAttribute aaa = new AuthorizeActionAttribute(actionsAttrib.Value);
                if (aaa.Authorize(HttpContext.Current))
                    hasAccess = true;
            }
            if (action != null)
            {
                action = parentAction + "." + action;
                hasAccess = AuthorizationChecker.HasAccess(action);
            }
            return hasAccess;
        }
        private static object CreateMenuIem(XmlNode node, Type itemType)
        {
            ConstructorInfo co = itemType.GetConstructor(Type.EmptyTypes);
            object menuItem = co.Invoke(null);
            PropertyInfo textProp = itemType.GetProperty("Text");
            if (node.Attributes["title"] != null)
                textProp.SetValue(menuItem, node.Attributes["title"].Value, null);
            PropertyInfo urlProp = itemType.GetProperty("NavigateUrl");
            if (node.Attributes["url"] != null)
                urlProp.SetValue(menuItem, WebUtility.GetFullAbsolutePath(node.Attributes["url"].Value), null);
            PropertyInfo tooltipProp = itemType.GetProperty("ToolTip");
            if (node.Attributes["description"] != null)
                tooltipProp.SetValue(menuItem, node.Attributes["description"].Value, null);
            PropertyInfo info5 = itemType.GetProperty("ImageUrl");
            if ((node.Attributes["imageurl"] != null) && (info5 != null))
                info5.SetValue(menuItem, WebUtility.GetFullAbsolutePath(node.Attributes["imageurl"].Value), null);
            PropertyInfo info6 = itemType.GetProperty("Target");
            if ((node.Attributes["target"] != null) && (info6 != null))
                info6.SetValue(menuItem, node.Attributes["target"].Value, null);
            PropertyInfo propValue = itemType.GetProperty("Value");
            if ((node.Attributes["action"] != null) && (propValue != null))
            {
                propValue.SetValue(menuItem, node.Attributes["action"].Value, null);

            }
            return menuItem;
        }
        public static bool HasAnyOf(this string[] array1, string[] array2)
        {
            foreach (string s in array2)
            {
                if (Array.IndexOf(array1, s) != -1)
                    return true;
            }
            return false;
        }

        public static object GetRandomGUID()
        {
            return Guid.NewGuid().ToString().Replace("-", "_");
        }
    }

}
