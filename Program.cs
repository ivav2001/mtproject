using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace scsc
{
    public class Table
    {
        private Stack<Dictionary<string, TableSymbol>> symbolTable;
        private Dictionary<string, TableSymbol> fieldScope;
        private Dictionary<string, TableSymbol> universeScope;
        private List<string> usingNamespaces = new List<string>();
        private List<string> references;

        public Table(List<string> references)
        {
            this.symbolTable = new Stack<Dictionary<string, TableSymbol>>();
            this.universeScope = BeginScope();
            this.fieldScope = BeginScope();
            this.references = references;
            foreach (string assemblyRef in references)
            {
                Assembly.LoadWithPartialName(assemblyRef);
            }

            AddFunction(new IdentToken(0, 0, "Abs"), typeof(int), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "value"), typeof(int), null) }, null);
            AddFunction(new IdentToken(0, 0, "Sqr"), typeof(int), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "value"), typeof(int), null) }, null);
            AddFunction(new IdentToken(0, 0, "Odd"), typeof(bool), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "value"), typeof(int), null) }, null);
            AddFunction(new IdentToken(0, 0, "Ord"), typeof(int), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "value"), typeof(char), null) }, null);
            AddFunction(new IdentToken(0, 0, "Scanf"), typeof(void), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "format"), typeof(string), null) }, null);
            AddFunction(new IdentToken(0, 0, "Printf"), typeof(void), new List<FormalParamSymbol> { new FormalParamSymbol(new IdentToken(0, 0, "format"), typeof(string), null) }, null);
        }

        public override string ToString()
        {
            StringBuilder s = new StringBuilder();
            int i = symbolTable.Count;
            s.AppendLine("=========");
            foreach (Dictionary<string, TableSymbol> table in symbolTable)
            {
                s.AppendLine($"---[{i--}]---");
                foreach (KeyValuePair<string, TableSymbol> row in table)
                {
                    s.AppendLine($"[{row.Key}] {row.Value}");
                }
            }
            s.AppendLine("=========");
            return s.ToString();
        }

        public void AddUsingNamespace(string usingNamespace)
        {
            usingNamespaces.Add(usingNamespace);
        }

        public TableSymbol Add(TableSymbol symbol)
        {
            symbolTable.Peek().Add(symbol.value, symbol);
            return symbol;
        }

        public TableSymbol AddToUniverse(TableSymbol symbol)
        {
            universeScope.Add(symbol.value, symbol);
            return symbol;
        }

        public FieldSymbol AddField(IdentToken token, FieldInfo field)
        {
            FieldSymbol result = new FieldSymbol(token, field);
            fieldScope.Add(token.value, result);
            return result;
        }

        public LocalVarSymbol AddLocalVar(IdentToken token, LocalBuilder localBuilder)
        {
            LocalVarSymbol result = new LocalVarSymbol(token, localBuilder);
            symbolTable.Peek().Add(token.value, result);
            return result;
        }

        public FormalParamSymbol AddFormalParam(IdentToken token, Type type, ParameterBuilder parameterInfo)
        {
            FormalParamSymbol result = new FormalParamSymbol(token, type, parameterInfo);
            symbolTable.Peek().Add(token.value, result);
            return result;
        }

        public MethodSymbol AddMethod(IdentToken token, Type type, FormalParamSymbol[] formalParams, MethodInfo methodInfo)
        {
            MethodSymbol result = new MethodSymbol(token, type, formalParams, methodInfo);
            symbolTable.Peek().Add(token.value, result);
            return result;
        }

        public FunctionSymbol AddFunction(IdentToken token, Type type, List<FormalParamSymbol> formalParams, MethodInfo methodInfo)
        {
            FunctionSymbol result = new FunctionSymbol(token, type, formalParams, methodInfo);
            symbolTable.Peek().Add(token.value, result);
            return result;
        }

        public TableSymbol AddBuiltInType(IdentToken token, string typeName)
        {
            Type builtInType = ResolveBuiltInType(typeName);
            if (builtInType != null)
            {
                PrimitiveTypeSymbol result = new PrimitiveTypeSymbol(token, builtInType);
                AddToUniverse(result);
                return result;
            }
            else
            {
                return null;
            }
        }

        public Type ResolveBuiltInType(string typeName)
        {
            if (typeName == "*")
            {
                return typeof(IntPtr);
            }
            else if (typeName == "pchar")
            {
                return typeof(char);
            }
            else
            {
                return null;
            }
        }

        public Dictionary<string, TableSymbol> BeginScope()
        {
            symbolTable.Push(new Dictionary<string, TableSymbol>());
            return symbolTable.Peek();
        }

        public void EndScope()
        {
            Debug.WriteLine(ToString());
            symbolTable.Pop();
        }

        public TableSymbol GetSymbol(string ident)
        {
            foreach (Dictionary<string, TableSymbol> table in symbolTable)
            {
                if (table.TryGetValue(ident, out var result))
                {
                    return result;
                }
            }
            return ResolveExternalMember(ident);
        }

        public Type ResolveExternalType(string ident)
        {
            Type type = Type.GetType(ident, false, false);
            if (type != null) return type;
            foreach (string ns in usingNamespaces)
            {
                string nsTypeName = ns + Type.Delimiter + ident;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(ident);
                    if (type != null) return type;
                    type = assembly.GetType(nsTypeName);
                    if (type != null) return type;
                }
            }
            return null;
        }

        public TableSymbol ResolveExternalMember(string ident)
        {
            int lastIx = ident.LastIndexOf(Type.Delimiter);
            if (lastIx > 0)
            {
                string memberName = ident.Substring(lastIx + 1);
                string typeName = ident.Substring(0, lastIx);

                Type type = ResolveExternalType(typeName);
                if (type != null)
                {
                    FieldInfo fi = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
                    if (fi != null) return new FieldSymbol(new IdentToken(0, 0, memberName), fi);
                    MemberInfo[] mi = type.GetMember(memberName, MemberTypes.Method, BindingFlags.Public | BindingFlags.Static);
                    if (mi != null) return new ExternalMethodSymbol(new IdentToken(0, 0, memberName), (MethodInfo[])mi);
                }
            }

            return null;
        }
    }

    // Placeholders for missing symbol classes.
    public class TableSymbol { public string value; }
    public class IdentToken { public IdentToken(int line, int column, string value) { this.value = value; } public string value; }
    public class FieldSymbol : TableSymbol { public FieldSymbol(IdentToken token, FieldInfo field) { } }
    public class LocalVarSymbol : TableSymbol { public LocalVarSymbol(IdentToken token, LocalBuilder localBuilder) { } }
    public class FormalParamSymbol : TableSymbol { public FormalParamSymbol(IdentToken token, Type type, ParameterBuilder parameterInfo) { } }
    public class MethodSymbol : TableSymbol { public MethodSymbol(IdentToken token, Type type, FormalParamSymbol[] formalParams, MethodInfo methodInfo) { } }
    public class FunctionSymbol : TableSymbol { public FunctionSymbol(IdentToken token, Type type, List<FormalParamSymbol> formalParams, MethodInfo methodInfo) { } }
    public class PrimitiveTypeSymbol : TableSymbol { public PrimitiveTypeSymbol(IdentToken token, Type type) { } }
    public class ExternalMethodSymbol : TableSymbol { public ExternalMethodSymbol(IdentToken token, MethodInfo[] methods) { } }

    // Main method to test the Table class.
    public class Program
    {
        public static void Main(string[] args)
        {
            // Create a list of references (can be empty for this test).
            List<string> references = new List<string>();

            // Create a new Table object
            Table symbolTable = new Table(references);

            // Test the AddUsingNamespace method
            symbolTable.AddUsingNamespace("System");

            // Test adding a symbol (this will add it to the current scope)
            TableSymbol symbol = new TableSymbol { value = "mySymbol" };
            symbolTable.Add(symbol);

            // Output the current state of the symbol table
            Console.WriteLine(symbolTable.ToString());

            // Add a built-in type (e.g., "int")
            symbolTable.AddBuiltInType(new IdentToken(0, 0, "int"), "int");

            // Output the updated symbol table
            Console.WriteLine(symbolTable.ToString());
        }
    }
}

