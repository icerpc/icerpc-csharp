exports.postTransform = function (model) {

    if(model.isEnum) {

        var childrens = model.children[0].children;

        // Extract the enumerator value and set the "_enum_value" property.
        childrens.forEach(function (item) {
            const regex = /[\w\d]+\s?=\s?(\d+),?/gm;
            var m = regex.exec(item.syntax.content[0].value);
            if (m !== null) {
                item._enum_value = parseInt(m[1]);
            }
        });

        const compare_enum_value = function( a, b ) {
            if ( a._enum_value < b._enum_value ) {
                return -1;
            }
            if ( a._enum_value > b._enum_value ) {
                return 1;
            }
            return 0;
        }

        // Sort the enumerators in ascending order based on their values."
        model.children[0].children = childrens.sort(compare_enum_value);
    }

    return model;
}
