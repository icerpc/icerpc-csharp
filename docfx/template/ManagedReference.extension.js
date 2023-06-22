exports.postTransform = function (model) {

    if(model.isEnum) {

        let children = model.children[0].children;

        // Extract the enumerator value and set the "_enum_value" property.
        children.forEach((item) => {
            const regex = /[\w\d]+\s?=\s?(\d+),?/gm;
            let m = regex.exec(item.syntax.content[0].value);
            if (m !== null) {
                item._enum_value = parseInt(m[1]);
            }
        });

        // Sort the enumerators in ascending order based on their values."
        model.children[0].children = children.sort((lhs, rhs) => (lhs._enum_value - rhs._enum_value));
    }

    return model;
}
