using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using SubSonic.Linq.Structure;
using SubSonic.Query;

namespace SubSonic.Extensions
{
	public class QueryVisitor2 : ExpressionVisitor
	{
		private Stack<ConstraintType> _conditionalConstraintTypeStack;
		private Stack<bool> _binaryDirectionIsLeftStack;
		private List<Constraint> _constraints;

		public IList<Constraint> GetConstraints(Expression expression)
		{
			_conditionalConstraintTypeStack = new Stack<ConstraintType>();
			_binaryDirectionIsLeftStack = new Stack<bool>();
			_constraints = new List<Constraint>();
			Visit(expression);
			return _constraints;
		}

		protected override Expression VisitConstant(ConstantExpression constant)
		{
			if (_binaryDirectionIsLeftStack.Peek())
			{
				_constraints.Last().ParameterName = constant.Value.ToString();
				_constraints.Last().ColumnName = constant.Value.ToString();
			}
			else
			{
				_constraints.Last().ParameterValue = constant.Value;
			}

			return constant;
		}

		protected override Expression VisitMemberAccess(MemberExpression member)
		{
			Expression expression = member;

			if (_binaryDirectionIsLeftStack.Peek())
			{
				_constraints.Last().ColumnName = member.Member.Name;
				_constraints.Last().ParameterName = member.Member.Name;
				_constraints.Last().ConstructionFragment = member.Member.Name;
			}
			else
			{
				ConstantExpression constant;
				expression = constant = Evaluate(member);
				_constraints.Last().ParameterValue = constant.Value;
			}

			return expression;
		}

		protected override Expression VisitBinary(BinaryExpression binary)
		{
			binary = ConvertVbCompareString(binary);

			bool isConditional = ExpressionTypeIsConditional(binary);
			bool isComparison = ExpressionTypeIsComparison(binary);

			Expression bLeft = binary.Left;
			Expression bRight = binary.Right;

			if (isConditional && ExpressionTypeIsComparison(binary.Left))
			{
				Expression tempExp = bRight;
				bRight = bLeft;
				bLeft = tempExp;
			}

			if (isComparison)
			{
				ConstraintType constraintType = (_conditionalConstraintTypeStack.Count > 0 ? _conditionalConstraintTypeStack.Peek() : ConstraintType.Where);
				_constraints.Add(new Constraint() { Condition = constraintType, Comparison = ToComparison(binary.NodeType) });
			}

			_binaryDirectionIsLeftStack.Push(true);

			Expression left = Visit(bLeft);

			_binaryDirectionIsLeftStack.Pop();

			if (ExpressionTypeIsComparison(bLeft))
				_constraints.Last().HasOpeningParantheses = true;

			if (isConditional)
				_conditionalConstraintTypeStack.Push(ToConstraintType(binary.NodeType));

			_binaryDirectionIsLeftStack.Push(false);

			Expression right = Visit(bRight);

			_binaryDirectionIsLeftStack.Pop();

			if (ExpressionTypeIsComparison(bRight))
				_constraints.Last().HasClosingParantheses = true;

			if (isConditional)
				_conditionalConstraintTypeStack.Pop();
	
			Expression conversion = Visit(binary.Conversion);

			if (isComparison && _constraints.Last().ParameterValue == null)
			{
				switch (binary.NodeType)
				{
					case ExpressionType.Equal:
						_constraints.Last().Comparison = Comparison.Is;
						break;
					case ExpressionType.NotEqual:
						_constraints.Last().Comparison = Comparison.IsNot;
						break;
				}
			}

			if (left != bLeft || right != bRight || conversion != binary.Conversion)
			{
				return (binary.NodeType == ExpressionType.Coalesce
					? Expression.Coalesce(left, right, conversion as LambdaExpression)
					: Expression.MakeBinary(binary.NodeType, left, right, binary.IsLiftedToNull, binary.Method));
			}

			return binary;
		}

		private static bool ExpressionTypeIsConditional(Expression expression)
		{
			return (expression.NodeType == ExpressionType.And
				|| expression.NodeType == ExpressionType.AndAlso
				|| expression.NodeType == ExpressionType.Or
				|| expression.NodeType == ExpressionType.OrElse);
		}

		private static bool ExpressionTypeIsComparison(Expression expression)
		{
			return (expression.NodeType == ExpressionType.Equal
				|| expression.NodeType == ExpressionType.NotEqual
				|| expression.NodeType == ExpressionType.LessThan
				|| expression.NodeType == ExpressionType.LessThanOrEqual
				|| expression.NodeType == ExpressionType.GreaterThan
				|| expression.NodeType == ExpressionType.GreaterThanOrEqual);
		}

		//#region Copied "unmodified" from old QueryVisitor class

		//private bool _isNot;

		//protected override Expression VisitUnary(UnaryExpression u)
		//{
		//    _isNot = true;   // TODO: This is quick fix - Unary handling has to be reworked!

		//    if (u.NodeType == ExpressionType.Not)
		//    {
		//        //this is a "!" not operator, which is akin to saying "member.name == false"
		//        //so we'll switch it up
		//        MemberExpression member = u.Operand as MemberExpression;
		//        if (member != null)
		//        {
		//            Constraint constraint = new Constraint();
		//            constraint.ParameterName = member.Member.Name;
		//            constraint.ColumnName = member.Member.Name;
		//            constraint.ConstructionFragment = member.Member.Name;
		//            constraint.ParameterValue = false;
		//            _constraints.Add(constraint);
		//        }
		//    }

		//    Expression unaryResult = base.VisitUnary(u);

		//    _isNot = false;

		//    return unaryResult;
		//}

		//protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
		//{
		//    // TODO: Here we only support member expressions -> Extend to solve http://github.com/subsonic/SubSonic-3.0/issues#issue/59
		//    Expression result = methodCallExpression;
		//    var obj = methodCallExpression.Object as MemberExpression;
		//    if (obj != null)
		//    {
		//        var constraint = new Constraint();
		//        switch (methodCallExpression.Method.Name)
		//        {
		//            case "Contains":
		//                constraint.Comparison = Comparison.Like;
		//                break;
		//            case "EndsWith":
		//                constraint.Comparison = Comparison.EndsWith;
		//                break;
		//            case "StartsWith":
		//                constraint.Comparison = Comparison.StartsWith;
		//                break;
		//            default:
		//                return base.VisitMethodCall(methodCallExpression);
		//        }
		//        // Set the starting / ending wildcards on the parameter value... not the best place to do this, but I'm 
		//        // attempting to constrain the scope of the change.
		//        constraint.ConstructionFragment = obj.Member.Name;
		//        // Set the current constraint... Visit will be using it, I don't know what it would do with multiple args....
		//        current = constraint;
		//        foreach (var arg in methodCallExpression.Arguments)
		//        {
		//            isLeft = false;
		//            Visit(arg);
		//        }
		//        isLeft = true;
		//        // After Visit, the current constraint will have some parameters, so set the wildcards on the parameter.
		//        SetConstraintWildcards(constraint);
		//    }
		//    else
		//    {
		//        switch (methodCallExpression.Method.Name)
		//        {
		//            case "Contains":
		//            case "Any":
		//                BuildCollectionConstraint(methodCallExpression);
		//                break;
		//            default:
		//                throw new InvalidOperationException(
		//                    String.Format("Method {0} is not supported in linq statement!",
		//                    methodCallExpression.Method.Name));
		//        }
		//    }

		//    AddConstraint();
		//    return methodCallExpression;
		//}

		//private void BuildCollectionConstraint(MethodCallExpression methodCallExpression)
		//{
		//    if (methodCallExpression.Arguments.Count == 2)
		//    {
		//        isLeft = true;
		//        Visit(methodCallExpression.Arguments[1]);

		//        isLeft = false;

		//        var c = Visit(methodCallExpression.Arguments[0]) as ConstantExpression;

		//        if (c != null)
		//        {
		//            // Constants
		//            current.InValues = c.Value as IEnumerable;
		//        }
		//        else
		//        {
		//            // something parsed to parameter values
		//            current.InValues = current.ParameterValue as IEnumerable;
		//        }

		//        current.Comparison = _isNot ? Comparison.NotIn : Comparison.In;

		//        if (current.InValues == null || !current.InValues.GetEnumerator().MoveNext())
		//        {
		//            current = BuildAlwaysFalseConstraint();
		//        }
		//    }
		//}

		//private Constraint BuildAlwaysFalseConstraint()
		//{
		//    var falseConstraint = new Constraint();

		//    falseConstraint.ConstructionFragment = "1";
		//    falseConstraint.ParameterValue = 0;
		//    falseConstraint.ColumnName = String.Empty;
		//    falseConstraint.Comparison = Comparison.Equals;

		//    return falseConstraint;
		//}

		//protected void SetConstraintWildcards(Constraint constraint)
		//{
		//    if (constraint.ParameterValue is string)
		//    {
		//        switch (constraint.Comparison)
		//        {
		//            case Comparison.StartsWith:
		//                constraint.ParameterValue = constraint.ParameterValue + "%";
		//                break;
		//            case Comparison.EndsWith:
		//                constraint.ParameterValue = "%" + constraint.ParameterValue;
		//                break;
		//            case Comparison.Like:
		//                constraint.ParameterValue = "%" + constraint.ParameterValue + "%";
		//                break;
		//        }
		//    }
		//}

		//#endregion

		#region Evaluation

		private static ConstantExpression Evaluate(Expression expression)
		{
			if (expression.NodeType == ExpressionType.Constant)
				return (ConstantExpression) expression;
			Type expressionType = expression.Type;
			if (expressionType.IsValueType)
				expression = Expression.Convert(expression, typeof(object));
			Expression<Func<object>> lambda = Expression.Lambda<Func<object>>(expression);
			Func<object> func = lambda.Compile();
			return Expression.Constant(func(), expressionType);
		}

		#endregion

		#region Conversion

		private static ConstraintType ToConstraintType(ExpressionType expressionType)
		{
			switch (expressionType)
			{
				case ExpressionType.And:
				case ExpressionType.AndAlso:
					return ConstraintType.And;
				case ExpressionType.Or:
				case ExpressionType.OrElse:
					return ConstraintType.Or;
				default:
					throw new ArgumentException("Expression type is not a valid constraint type");
			}
		}

		private static Comparison ToComparison(ExpressionType expressionType)
		{
			switch (expressionType)
			{
				case ExpressionType.Equal:
					return Comparison.Equals;
				case ExpressionType.NotEqual:
					return Comparison.NotEquals;
				case ExpressionType.LessThan:
					return Comparison.LessThan;
				case ExpressionType.LessThanOrEqual:
					return Comparison.LessOrEquals;
				case ExpressionType.GreaterThan:
					return Comparison.GreaterThan;
				case ExpressionType.GreaterThanOrEqual:
					return Comparison.GreaterOrEquals;
				default:
					throw new ArgumentException("Expression type is not a valid comparison type");
			}
		}

		#endregion
	}
}
